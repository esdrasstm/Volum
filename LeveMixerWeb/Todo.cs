using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Volum;

sealed class TodoItem
{
    public string Id { get; set; } = "";
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public long Created { get; set; }   // milissegundos desde 1970 (ordem da lista)
}

/// <summary>
/// Lista de tarefas da ilha (página "Tarefas"): anotar, riscar e apagar. Fica num arquivo separado,
/// %AppData%\Volum\todo.json (como a água: o settings.json é regravado inteiro por outras janelas).
/// Nada roda sozinho: só muda quando a pessoa mexe na ilha.
/// </summary>
static class Todo
{
    const int MaxItems = 60, MaxText = 120;
    static readonly string FilePath = Path.Combine(AppSettings.DataFolder, "todo.json");
    static readonly List<TodoItem> s_items = Load();

    /// <summary>A lista mudou (a ilha redesenha).</summary>
    public static event Action? Changed;

    public static IReadOnlyList<TodoItem> Items => s_items;

    public static void Add(string? text)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > MaxText) text = text[..MaxText];
        if (s_items.Count >= MaxItems) s_items.RemoveAll(i => i.Done);   // cheia: abre espaço tirando as feitas
        if (s_items.Count >= MaxItems) return;
        s_items.Add(new TodoItem { Id = Guid.NewGuid().ToString("N")[..12], Text = text, Created = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        Commit();
    }

    public static void Toggle(string? id)
    {
        var item = s_items.FirstOrDefault(i => i.Id == id);
        if (item == null) return;
        item.Done = !item.Done;
        UiSound.Play(item.Done ? UiSound.Kind.TaskDone : UiSound.Kind.TaskUndo);   // antes de gravar: o som sai na hora
        Commit();
    }

    public static void Remove(string? id)
    {
        if (s_items.RemoveAll(i => i.Id == id) > 0) Commit();
    }

    public static void ClearDone()
    {
        if (s_items.RemoveAll(i => i.Done) > 0) Commit();
    }

    static void Commit()
    {
        Save();
        Changed?.Invoke();
    }

    static List<TodoItem> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<TodoItem>>(File.ReadAllText(FilePath)) ?? new List<TodoItem>();
        }
        catch { }
        return new List<TodoItem>();
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(s_items, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
