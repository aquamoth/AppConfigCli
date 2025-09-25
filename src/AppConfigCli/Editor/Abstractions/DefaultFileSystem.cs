namespace AppConfigCli.Editor.Abstractions;

internal sealed class DefaultFileSystem : IFileSystem
{
    public string GetTempPath() => System.IO.Path.GetTempPath();
    public string Combine(params string[] parts) => System.IO.Path.Combine(parts);
    public void WriteAllText(string path, string contents) => System.IO.File.WriteAllText(path, contents);
    public string[] ReadAllLines(string path) => System.IO.File.ReadAllLines(path);
    public bool Exists(string path) => System.IO.File.Exists(path);
    public void Delete(string path) { try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { } }
}
