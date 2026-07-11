using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Winotch.Tests;

public class ShelfTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ServiceCapsNewestItemsAtConfiguredLimit()
    {
        var shelf = new ShelfService(new ShelfSettings { Cap = 3 });

        for (var i = 0; i < 5; i++)
        {
            shelf.Stage(Text($"item {i}"));
        }

        Assert.Equal(3, shelf.Items.Count);
        Assert.Equal("item 4", shelf.Items[0].Preview);
        Assert.Equal("item 2", shelf.Items[^1].Preview);
    }

    [Fact]
    public void ServiceDedupesMatchingSignaturesAcrossShelf()
    {
        var shelf = new ShelfService(new ShelfSettings { Cap = 8 });

        shelf.Stage(Text("same", Now));
        shelf.Stage(Text("other", Now.AddMinutes(1)));
        shelf.Stage(Text("same", Now.AddMinutes(2)));

        Assert.Collection(
            shelf.Items,
            first => Assert.Equal("same", first.Preview),
            second => Assert.Equal("other", second.Preview));
    }

    [Fact]
    public void ServiceRemovesAndClearsItems()
    {
        var shelf = new ShelfService(new ShelfSettings { Cap = 8 });
        var first = Text("first");
        var second = Text("second");
        shelf.Stage(first);
        shelf.Stage(second);

        Assert.True(shelf.Remove(first.Id));
        Assert.Single(shelf.Items);
        Assert.True(shelf.Clear());
        Assert.Empty(shelf.Items);
        Assert.False(shelf.Clear());
    }

    [Fact]
    public void ServiceHonorsClipboardPrivacyExclusions()
    {
        var shelf = new ShelfService(new ShelfSettings { Cap = 8 });
        var formats = new FakeClipboardFormats()
            .With(ClipboardPrivacyPolicy.ExcludeClipboardContentFromMonitorProcessing, true);

        Assert.False(shelf.Stage(Text("private"), formats));
        Assert.Empty(shelf.Items);
    }

    [Fact]
    public void TextAndLinksAreClassifiedSeparately()
    {
        var text = ShelfItem.FromText("note", Now);
        var link = ShelfItem.FromText(" https://example.com/a ", Now);

        Assert.Equal(ShelfItemKind.Text, text!.Kind);
        Assert.Equal(ShelfItemKind.Link, link!.Kind);
        Assert.Equal("https://example.com/a", link.Preview);
    }

    [Fact]
    public void FileItemStoresOnlyPathsAndImageStoresOnlyThumbnail()
    {
        var files = ShelfItem.FromFiles([@"C:\temp\report.pdf", @"C:\temp\image.png"], Now);
        var thumbnail = new byte[] { 1, 2, 3 };
        var image = ShelfItem.FromImage(thumbnail, Now);
        thumbnail[0] = 9;

        Assert.NotNull(files);
        Assert.Equal(ShelfItemKind.Files, files.Kind);
        Assert.Null(files.ThumbnailPng);
        Assert.Equal("report.pdf +1", files.Preview);

        Assert.NotNull(image);
        Assert.Equal(ShelfItemKind.Image, image.Kind);
        Assert.Empty(image.FilePaths);
        Assert.Equal([1, 2, 3], image.ThumbnailPng);
    }

    [Fact]
    public void LaunchTargetsIncludeEveryFileInMultiFileShelfItem()
    {
        var item = ShelfItem.FromFiles([@"C:\temp\one.txt", @"C:\temp\two.txt"], Now)!;

        Assert.Equal([@"C:\temp\one.txt", @"C:\temp\two.txt"], ShelfLaunchTargets.For(item));
    }

    [Fact]
    public void LaunchTargetsUseLinkTextOnlyForLinks()
    {
        var link = ShelfItem.FromText("https://example.com", Now)!;
        var text = ShelfItem.FromText("just text", Now)!;

        Assert.Equal(["https://example.com"], ShelfLaunchTargets.For(link));
        Assert.Empty(ShelfLaunchTargets.For(text));
    }

    [Fact]
    public void ShelfAcceptsEverySupportedWindowsDropFormat()
    {
        var supported = new[]
        {
            StandardDataFormats.StorageItems,
            StandardDataFormats.Bitmap,
            StandardDataFormats.Text,
            StandardDataFormats.WebLink,
            StandardDataFormats.ApplicationLink
        };

        Assert.All(supported, format => Assert.True(ShelfService.SupportsDropFormats([format])));
    }

    [Fact]
    public void ShelfRejectsUnsupportedDropPayloads()
    {
        Assert.False(ShelfService.SupportsDropFormats([]));
        Assert.False(ShelfService.SupportsDropFormats([StandardDataFormats.Html]));
    }

    [Fact]
    public async Task ShelfStagesARealWindowsTextDropPayload()
    {
        var package = new DataPackage();
        package.SetText("dropped note");
        var shelf = new ShelfService(new ShelfSettings());

        Assert.True(await shelf.StageDropAsync(package.GetView(), Now));
        Assert.Equal("dropped note", Assert.Single(shelf.Items).Preview);
    }

    [Fact]
    public async Task ShelfStagesARealWindowsLinkDropPayload()
    {
        var package = new DataPackage();
        package.SetWebLink(new Uri("https://example.com/shelf"));
        var shelf = new ShelfService(new ShelfSettings());

        Assert.True(await shelf.StageDropAsync(package.GetView(), Now));
        var item = Assert.Single(shelf.Items);
        Assert.Equal(ShelfItemKind.Link, item.Kind);
        Assert.Equal("https://example.com/shelf", item.Text);
    }

    [Fact]
    public async Task ShelfStagesARealWindowsFileDropPayload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"winotch-shelf-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "shelf file");
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            var package = new DataPackage();
            package.SetStorageItems([file]);
            var shelf = new ShelfService(new ShelfSettings());

            Assert.True(await shelf.StageDropAsync(package.GetView(), Now));
            var item = Assert.Single(shelf.Items);
            Assert.Equal(ShelfItemKind.Files, item.Kind);
            Assert.Equal(path, Assert.Single(item.FilePaths), ignoreCase: true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ShelfStagesARealWindowsBitmapDropPayload()
    {
        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Straight,
            1,
            1,
            96,
            96,
            [0x22, 0x66, 0xCC, 0xFF]);
        await encoder.FlushAsync();
        stream.Seek(0);

        var package = new DataPackage();
        package.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
        var shelf = new ShelfService(new ShelfSettings());

        Assert.True(await shelf.StageDropAsync(package.GetView(), Now));
        var item = Assert.Single(shelf.Items);
        Assert.Equal(ShelfItemKind.Image, item.Kind);
        Assert.NotEmpty(item.ThumbnailPng!);
    }

    [Fact]
    public async Task ShelfStagesNativeFileTextLinkAndImagePayloads()
    {
        var shelf = new ShelfService(new ShelfSettings());
        var filePath = @"C:\temp\native-drop.txt";

        Assert.True(await shelf.StageNativeDropAsync(new NativeDropPayload([filePath], null, null), Now));
        Assert.Equal(filePath, Assert.Single(shelf.Items).FilePaths.Single());

        Assert.True(await shelf.StageNativeDropAsync(new NativeDropPayload([], "native note", null), Now));
        Assert.Equal(ShelfItemKind.Text, shelf.Items[0].Kind);

        Assert.True(await shelf.StageNativeDropAsync(
            new NativeDropPayload([], "https://example.com/native", null),
            Now));
        Assert.Equal(ShelfItemKind.Link, shelf.Items[0].Kind);

        using var stream = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(
            BitmapPixelFormat.Rgba8,
            BitmapAlphaMode.Straight,
            1,
            1,
            96,
            96,
            [0x33, 0x99, 0xEE, 0xFF]);
        await encoder.FlushAsync();
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size);
        var png = new byte[(int)stream.Size];
        reader.ReadBytes(png);

        Assert.True(await shelf.StageNativeDropAsync(new NativeDropPayload([], null, png), Now));
        Assert.Equal(ShelfItemKind.Image, shelf.Items[0].Kind);
        Assert.NotEmpty(shelf.Items[0].ThumbnailPng!);
    }

    [Fact]
    public void NativeDibReaderBuildsAWindowsBitmapFileHeader()
    {
        var dib = new byte[44];
        BitConverter.GetBytes(40u).CopyTo(dib, 0);
        BitConverter.GetBytes(1).CopyTo(dib, 4);
        BitConverter.GetBytes(1).CopyTo(dib, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(dib, 12);
        BitConverter.GetBytes((ushort)32).CopyTo(dib, 14);
        dib[40] = 0x22;
        dib[41] = 0x66;
        dib[42] = 0xCC;
        dib[43] = 0xFF;

        Assert.True(NativeDropDataReader.TryBuildBitmapFile(dib, out var bitmapFile));
        Assert.Equal((byte)'B', bitmapFile[0]);
        Assert.Equal((byte)'M', bitmapFile[1]);
        Assert.Equal(54u, BitConverter.ToUInt32(bitmapFile, 10));
        Assert.NotEmpty(NativeDropDataReader.DecodeDibToPng(dib)!);
    }

    [Fact]
    public void NativeOleReaderExtractsExplorerFilesAndUnicodeText()
    {
        var files = NativeDropDataReader.Read(FakeOleDataObject.ForFiles([
            @"C:\temp\one.txt",
            @"C:\temp\two.png"
        ]));
        Assert.Equal([@"C:\temp\one.txt", @"C:\temp\two.png"], files.FilePaths);
        Assert.Null(files.Text);

        var text = NativeDropDataReader.Read(FakeOleDataObject.ForUnicodeText("native dragged text"));
        Assert.Empty(text.FilePaths);
        Assert.Equal("native dragged text", text.Text);
    }

    private static ShelfItem Text(string value, DateTimeOffset? stagedAt = null) =>
        ShelfItem.FromText(value, stagedAt ?? Now)!;

    private sealed class FakeClipboardFormats : IClipboardFormatReader
    {
        private readonly Dictionary<string, object?> _formats = new(StringComparer.Ordinal);

        public FakeClipboardFormats With(string formatName, object? value)
        {
            _formats[formatName] = value;
            return this;
        }

        public bool HasFormat(string formatName) => _formats.ContainsKey(formatName);

        public bool TryGetData(string formatName, out object? data) =>
            _formats.TryGetValue(formatName, out data);
    }

    private sealed class FakeOleDataObject : IDataObject
    {
        private const uint GmemMoveable = 0x0002;
        private const uint GmemZeroInit = 0x0040;
        private readonly short _format;
        private readonly byte[] _data;

        private FakeOleDataObject(short format, byte[] data)
        {
            _format = format;
            _data = data;
        }

        public static FakeOleDataObject ForUnicodeText(string text) =>
            new(13, Encoding.Unicode.GetBytes(text + '\0'));

        public static FakeOleDataObject ForFiles(IReadOnlyList<string> paths)
        {
            var names = Encoding.Unicode.GetBytes(string.Join('\0', paths) + "\0\0");
            var dropFiles = new byte[20 + names.Length];
            BitConverter.GetBytes(20u).CopyTo(dropFiles, 0);
            BitConverter.GetBytes(1).CopyTo(dropFiles, 16);
            names.CopyTo(dropFiles, 20);
            return new FakeOleDataObject(15, dropFiles);
        }

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            if (QueryGetData(ref format) != 0)
            {
                throw new COMException("Unsupported test format", unchecked((int)0x80040064));
            }

            var memory = GlobalAlloc(GmemMoveable | GmemZeroInit, (UIntPtr)_data.Length);
            if (memory == IntPtr.Zero)
            {
                throw new OutOfMemoryException();
            }

            var pointer = GlobalLock(memory);
            try
            {
                Marshal.Copy(_data, 0, pointer, _data.Length);
            }
            finally
            {
                _ = GlobalUnlock(memory);
            }

            medium = new STGMEDIUM
            {
                tymed = TYMED.TYMED_HGLOBAL,
                unionmember = memory,
                pUnkForRelease = null
            };
        }

        public int QueryGetData(ref FORMATETC format) =>
            format.cfFormat == _format && (format.tymed & TYMED.TYMED_HGLOBAL) != 0
                ? 0
                : unchecked((int)0x80040064);

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) =>
            throw new NotSupportedException();

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            formatOut.ptd = IntPtr.Zero;
            return 1;
        }

        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release) =>
            throw new NotSupportedException();

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction) => throw new NotSupportedException();

        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
        {
            connection = 0;
            return unchecked((int)0x80040003);
        }

        public void DUnadvise(int connection) => throw new NotSupportedException();

        public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
        {
            enumAdvise = null!;
            return unchecked((int)0x80040003);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GlobalUnlock(IntPtr memory);
    }
}
