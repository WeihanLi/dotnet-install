namespace DotNetInstallManager.Services;

internal class InstallException : Exception
{
    public InstallException()
    {
    }

    public InstallException(string message)
        : base(message)
    {
    }

    public InstallException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
