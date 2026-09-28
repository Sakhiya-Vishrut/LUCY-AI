namespace LucyAI.Models
{
    /// <summary>
    /// General error display model — used by error dialogs and notifications.
    /// </summary>
    public class ErrorViewModel
    {
        public string? RequestId { get; set; }
        public string? ErrorMessage { get; set; }
        public string? StackTrace { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
        public bool ShowStackTrace => !string.IsNullOrEmpty(StackTrace);
    }
}
