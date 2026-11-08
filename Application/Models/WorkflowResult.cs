namespace Underfloor_heating_generator.Application.Models
{
    public sealed class WorkflowResult
    {
        private WorkflowResult(bool succeeded, string message)
        {
            Succeeded = succeeded;
            Message = message;
        }

        public bool Succeeded { get; }
        public string Message { get; }

        public static WorkflowResult Success(string message = "Трубы теплого пола построены.") =>
            new WorkflowResult(true, message);

        public static WorkflowResult Failure(string message) =>
            new WorkflowResult(false, message);
    }
}
