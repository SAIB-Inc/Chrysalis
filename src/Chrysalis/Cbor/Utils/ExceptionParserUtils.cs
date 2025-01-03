using System.Diagnostics;
using System.Text;

public static class ExceptionParserUtils
{
    public static string GetErrorInfo(Exception ex)
    {
        StringBuilder errorBuilder = new();
        Exception? currentEx = ex;
        HashSet<string> processedExceptions = [];
        
        while (currentEx != null)
        {
            if (processedExceptions.Add(currentEx.Message))
            {
                if (currentEx is InvalidOperationException)
                {
                    var frame = new StackTrace(currentEx, true)
                        .GetFrames()
                        ?.FirstOrDefault();

                    var fileName = frame?.GetFileName();
                    var lineNumber = frame?.GetFileLineNumber();
                    var location = fileName != null ? $" at {Path.GetFileName(fileName)}:{lineNumber}" : "";

                    errorBuilder.AppendLine($"    > Error{location}: {currentEx.Message}");
                }
            }

            currentEx = currentEx.InnerException;
        }

        return errorBuilder.ToString().TrimEnd();
    }
}