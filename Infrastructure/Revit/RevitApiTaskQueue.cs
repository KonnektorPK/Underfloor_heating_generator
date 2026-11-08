using System;
using Autodesk.Revit.UI;

namespace Underfloor_heating_generator.Infrastructure.Revit
{
    public sealed class RevitApiTaskQueue : IExternalEventHandler
    {
        private readonly object sync = new object();
        private readonly ExternalEvent externalEvent;
        private Action<UIApplication> pendingAction;

        public RevitApiTaskQueue()
        {
            externalEvent = ExternalEvent.Create(this);
        }

        public bool TrySchedule(Action<UIApplication> action, out string error)
        {
            if (action == null)
            {
                error = "Действие Revit не задано.";
                return false;
            }

            lock (sync)
            {
                if (pendingAction != null)
                {
                    error = "Дождитесь завершения текущей операции.";
                    return false;
                }
                pendingAction = action;
            }

            ExternalEventRequest request = externalEvent.Raise();
            if (request == ExternalEventRequest.Denied || request == ExternalEventRequest.TimedOut)
            {
                lock (sync)
                    pendingAction = null;
                error = "Revit не принял запрос на выполнение операции.";
                return false;
            }

            error = null;
            return true;
        }

        public void Execute(UIApplication application)
        {
            Action<UIApplication> action;
            lock (sync)
            {
                action = pendingAction;
                pendingAction = null;
            }

            action?.Invoke(application);
        }

        public string GetName() => "Генератор теплого пола: операция Revit";
    }
}
