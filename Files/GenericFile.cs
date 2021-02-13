using PhotoCopySort;
using System;
using System.IO;
using System.Security.Cryptography;

namespace PhotoCopy.Files
{
    class GenericFile : IFile
    {
        public FileInfo File { get; }

        public FileDateTime FileDateTime { get; }


        private string _sha256;
        public string Checksum => _sha256 ??= CalculateChecksumSha256();

        private string CalculateChecksumSha256()
        {
            using var stream = new BufferedStream(File.OpenRead(), 12000);
            var sha = new SHA256Managed();
            var checksum = sha.ComputeHash(stream);
            return BitConverter.ToString(checksum).Replace("-", string.Empty);
        }

        public GenericFile(FileInfo file, FileDateTime dateTime)
        {
            File = file ?? throw new ArgumentNullException(nameof(file));
            FileDateTime = dateTime;
        }


        public virtual void CopyTo(string newPath, bool isDryRun)
        {
            Log.Print($"{File.FullName} >> cp >> {newPath}", LogLevel.Verbose);
            if (!isDryRun)
            {
                File.CopyTo(newPath);
            }
        }

        public virtual void MoveTo(string newPath, bool isDryRun)
        {
            Log.Print($"{File.FullName} >> mv >> {newPath}", LogLevel.Verbose);
            if (!isDryRun)
            {
                File.MoveTo(newPath);
            }
        }
    }
}
