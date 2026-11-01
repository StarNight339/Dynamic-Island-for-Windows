using System.IO;
using DynamicIsland.Core;
using Xunit;

namespace DynamicIsland.Tests;

public sealed class FileShelfTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DynamicIslandTests", Guid.NewGuid().ToString("N"));
    private string ShelfPath => Path.Combine(_dir, "shelf.json");

    public FileShelfTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string MakeFile(string name)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, name);
        return path;
    }

    [Fact]
    public void Add_puts_new_files_first_and_keeps_drop_order()
    {
        var a = MakeFile("a.txt");
        var b = MakeFile("b.txt");
        var c = MakeFile("c.txt");
        var shelf = new FileShelf(ShelfPath);

        Assert.Equal(1, shelf.Add([a]));
        Assert.Equal(2, shelf.Add([b, c]));

        Assert.Equal(new[] { b, c, a }, shelf.Items);
    }

    [Fact]
    public void Add_dedupes_and_moves_existing_to_front()
    {
        var a = MakeFile("a.txt");
        var b = MakeFile("b.txt");
        var shelf = new FileShelf(ShelfPath);
        shelf.Add([a, b]);

        shelf.Add([b.ToUpperInvariant(), b]);

        Assert.Equal(2, shelf.Items.Count);
        Assert.Equal(b, shelf.Items[0], ignoreCase: true);
        Assert.Equal(a, shelf.Items[1]);
    }

    [Fact]
    public void Add_skips_missing_paths_but_accepts_folders()
    {
        var folder = Path.Combine(_dir, "folder");
        Directory.CreateDirectory(folder);
        var shelf = new FileShelf(ShelfPath);

        Assert.Equal(1, shelf.Add([Path.Combine(_dir, "nope.txt"), folder, ""]));
        Assert.Equal(new[] { folder }, shelf.Items);
    }

    [Fact]
    public void List_is_capped_at_MaxItems()
    {
        var shelf = new FileShelf(ShelfPath) { MaxItems = 2 };
        shelf.Add([MakeFile("1.txt")]);
        shelf.Add([MakeFile("2.txt")]);
        var newest = MakeFile("3.txt");
        shelf.Add([newest]);

        Assert.Equal(2, shelf.Items.Count);
        Assert.Equal(newest, shelf.Items[0]);

        shelf.MaxItems = 1;
        Assert.Equal(new[] { newest }, shelf.Items);
    }

    [Fact]
    public void Prune_drops_deleted_files()
    {
        var a = MakeFile("a.txt");
        var b = MakeFile("b.txt");
        var shelf = new FileShelf(ShelfPath);
        shelf.Add([a, b]);
        var changes = 0;
        shelf.Changed += () => changes++;

        File.Delete(a);

        Assert.True(shelf.Prune());
        Assert.Equal(new[] { b }, shelf.Items);
        Assert.False(shelf.Prune());
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Remove_and_Clear()
    {
        var a = MakeFile("a.txt");
        var b = MakeFile("b.txt");
        var shelf = new FileShelf(ShelfPath);
        shelf.Add([a, b]);

        shelf.Remove(a.ToUpperInvariant());
        Assert.Equal(new[] { b }, shelf.Items);

        shelf.Clear();
        Assert.Empty(shelf.Items);
        Assert.Empty(new FileShelf(ShelfPath).Items);
    }

    [Fact]
    public void Round_trips_through_the_file()
    {
        var a = MakeFile("a.txt");
        var b = MakeFile("b.txt");
        new FileShelf(ShelfPath).Add([a, b]);

        Assert.Equal(new[] { a, b }, new FileShelf(ShelfPath).Items);
    }

    [Fact]
    public void Corrupt_file_loads_empty_and_is_backed_up()
    {
        File.WriteAllText(ShelfPath, "{ not json");

        var shelf = new FileShelf(ShelfPath);

        Assert.Empty(shelf.Items);
        Assert.True(File.Exists(ShelfPath + ".bak"));
    }
}
