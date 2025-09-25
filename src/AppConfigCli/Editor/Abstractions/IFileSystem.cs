namespace AppConfigCli.Editor.Abstractions;

internal interface IFileSystem
{
    string GetTempPath();
    string Combine(params string[] parts);
    void WriteAllText(string path, string contents);
    string[] ReadAllLines(string path);
    bool Exists(string path);
    void Delete(string path);
}
