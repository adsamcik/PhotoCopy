using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PhotoCopy.Files;

internal record class GenericFile(FileInfo File, FileDateTime FileDateTime) : IFile
{
    private string _sha256;

    public string Checksum => _sha256 ??= CalculateChecksumSha256();

    private string CalculateChecksumSha256()
    {
        using var stream = new BufferedStream(File.OpenRead(), 65536);
        using var sha = SHA256.Create();
        byte[] checksum = sha.ComputeHash(stream);

        var sb = new StringBuilder(checksum.Length * 2);
        foreach (var b in checksum)
            sb.Append(b.ToString("x2"));

        return sb.ToString();
    }

    public virtual void CopyTo(string newPath, bool isDryRun)
    {
        Log.Print($"cp {File.FullName} --> {newPath}", Options.LogLevel.verbose);
        if (isDryRun)
        {
            try
            {
                _ = File.Attributes;
            }
            catch (UnauthorizedAccessException)
            {
                Log.Print($"Cannot read file {File.FullName}.", Options.LogLevel.errorsOnly);
            }
        }
        else
        {
            File.CopyTo(newPath);
        }
    }

    public virtual void MoveTo(string newPath, bool isDryRun)
    {
        Log.Print($"mv {File.FullName} --> {newPath}", Options.LogLevel.verbose);
        if (isDryRun)
        {
            try
            {
                _ = File.Attributes;
            }
            catch (UnauthorizedAccessException)
            {
                Log.Print($"Cannot read file {File.FullName}.", Options.LogLevel.errorsOnly);
            }
        } else
        {
            File.MoveTo(newPath);
        }
    }
}