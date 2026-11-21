using SoftBox.PhoneLab.Core.Updates;

if (args.Length != 7)
{
    Console.Error.WriteLine("Usage: SoftBox.ReleaseSigner <publish-directory> <version> <minimum-supported-version> <main-executable-relative-path> <private-key-pem-path> <output-zip> <release-notes-file>");
    return 2;
}

try
{
    var privateKeyPath = Path.GetFullPath(args[4]);
    if (!File.Exists(privateKeyPath))
        throw new FileNotFoundException("RSA publisher private key file not found.", privateKeyPath);
    if ((File.GetAttributes(privateKeyPath) & FileAttributes.ReparsePoint) != 0)
        throw new InvalidDataException("Publisher private key must not be a symbolic link or reparse point.");

    var privateKey = await File.ReadAllTextAsync(privateKeyPath);
    var releaseNotes = File.Exists(args[6]) ? await File.ReadAllTextAsync(args[6]) : "";
    var result = await SignedUpdateReleaseBuilder.BuildAsync(
        args[0],
        args[5],
        args[1],
        args[2],
        args[3],
        releaseNotes,
        privateKey);

    Console.WriteLine("SIGNED SOFTBOX WINDOWS UPDATE CREATED");
    Console.WriteLine("VERSION: " + result.Version);
    Console.WriteLine("PUBLISHER KEY ID: " + result.PublisherKeyId);
    Console.WriteLine("FILES: " + result.FileCount);
    Console.WriteLine("ARCHIVE BYTES: " + result.ArchiveBytes);
    Console.WriteLine("ARCHIVE SHA-256: " + result.ArchiveSha256);
    Console.WriteLine("OUTPUT: " + result.ArchivePath);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("Release signing failed: " + ex.Message);
    return 1;
}
