namespace TeamManager.Web;

internal sealed record UploadedFile(string Name, byte[] Content)
{
    private const int MaxSize = 10 * 1024 * 1024;

    public static async Task<UploadedFile> Read(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length is < 1 or > MaxSize)
            throw new ArgumentException("Odaberi datoteku veličine od 1 bajta do 10 MiB.");

        var name = Path.GetFileName(file.FileName.Replace('\\', '/')).Trim();
        if (name.Length is < 1 or > 200 || name is "." or ".." || name.Any(char.IsControl))
            throw new ArgumentException("Naziv datoteke nije valjan.");

        await using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length != file.Length)
            throw new ArgumentException("Datoteka nije učitana u cijelosti.");
        return new(name, buffer.ToArray());
    }
}