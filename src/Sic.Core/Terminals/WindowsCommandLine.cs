namespace Sic.Core.Terminals;

public static class WindowsCommandLine
{
    public static string QuoteArgument(string argument)
    {
        ArgumentNullException.ThrowIfNull(argument);
        if (argument.Contains('\0'))
        {
            throw new ArgumentException("Windows arguments cannot contain NUL characters.", nameof(argument));
        }

        var result = new System.Text.StringBuilder();
        result.Append('"');
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }

            if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
            }
            else
            {
                result.Append('\\', backslashes);
                result.Append(character);
            }
            backslashes = 0;
        }
        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }
}
