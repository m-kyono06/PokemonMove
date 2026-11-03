using System.Buffers.Binary;
using System.Security.Cryptography;
using PKHeX.Core;
using PokemonMove.Domain;
using PokemonMove.Engine;
using PokemonMove.Infrastructure;
using Xunit;

namespace PokemonMove.Infrastructure.Tests;

public sealed class TransferTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PokemonMove-transfer-tests-" + Guid.NewGuid());
    private LocalBankStore Bank() => new(root, new HgssPokemonCodec(), new HgssSaveEditor());
    private static readonly PokemonLocation Box = new(false, 0, 0);
    private static byte[] Hash(byte[] bytes) => SHA256.HashData(bytes);
    private static SaveSnapshot View(byte[] data) => new HgssSaveReader().Read(data);

    private static byte[] Save(bool dsv = false, bool optionalDamage = false)
    {
        var save = new SAV4HGSS(new byte[HgssSaveReader.RawSize]) { Version = GameVersion.SS, OT = "TEST" };
        save.Magic = SAV4.MAGIC_JAPAN_INTL;
        foreach (var partition in new[] { 0, 0x40000 })
            BinaryPrimitives.WriteInt32LittleEndian(save.Data.Slice(partition + SAV4HGSS.GeneralSize - 0xC, 4), SAV4HGSS.GeneralSize);
        foreach (var (species, slot) in new[] { ((ushort)158, 0), ((ushort)155, 1) })
        {
            var pk = new PK4 { Species = species, CurrentLevel = 12, PID = (uint)(slot + 1) };
            pk.ResetPartyStats();
            save.SetPartySlotAtIndex(pk, slot);
        }
        save.SetBoxSlotAtIndex(new PK4 { Species = 152, CurrentLevel = 10, PID = 50 }, 0, 0);
        var raw = save.Write().ToArray();
        if (optionalDamage) raw[0x27010] ^= 0xFF;
        if (!dsv) return raw;
        var data = new byte[raw.Length + 0x7A]; raw.CopyTo(data, 0);
        "|-DESMUME SAVE-|"u8.CopyTo(data.AsSpan(data.Length - "|-DESMUME SAVE-|"u8.Length));
        return data;
    }

    private sealed class File : ISaveDocument
    {
        public string Id => "test:same-save";
        public byte[] Data = Save();
        public int Writes;
        public string Failure = "";
        public Action? AfterWrite;
        public Task<byte[]> ReadAsync() => Task.FromResult(Data.ToArray());
        public Task WriteAsync(byte[] data)
        {
            Writes++;
            if (Failure == "before") throw new IOException("before write");
            if (Failure == "partial") { Data = data[..100]; throw new IOException("partial write"); }
            Data = data.ToArray(); AfterWrite?.Invoke();
            if (Failure == "after") throw new IOException("lost acknowledgement");
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DepositAndReturnToOriginalSavePreserveDataAndPreventBackupReplay(bool dsv)
    {
        var original = Save(dsv, optionalDamage: true);
        var file = new File { Data = original.ToArray() };
        var bank = Bank();
        var pokemon = View(original).Boxes[0].Slots[0].StoredData;
        await bank.MoveIntoBankAsync(file, Box, Hash(original));
        Assert.Equal((ushort)0, View(file.Data).Boxes[0].Slots[0].Species);
        var stored = Assert.Single(await Bank().ListAsync());
        Assert.True(stored.Entry.TransferEligible);
        Assert.Equal(pokemon, stored.Entry.Data);
        Assert.Equal(original, (await bank.LastSaveBackupAsync()).Data);
        // Unused extra blocks and the DSV footer remain byte-for-byte unchanged.
        Assert.Equal(original[0x23000..0x40000], file.Data[0x23000..0x40000]);
        Assert.Equal(original[HgssSaveReader.RawSize..], file.Data[HgssSaveReader.RawSize..]);
        Assert.Single(View(file.Data).Warnings);
        var backup = await bank.ExportAsync();
        await bank.MoveToSaveAsync(stored.Entry.Id, file, Box, Hash(file.Data));
        Assert.Empty(await bank.ListAsync());
        Assert.Equal(pokemon, View(file.Data).Boxes[0].Slots[0].StoredData);
        Assert.Equal(0, await bank.ImportAsync(backup));
        Assert.Empty(await Bank().ListAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => bank.MoveToSaveAsync(stored.Entry.Id, file, Box, Hash(file.Data)));
        await Assert.ThrowsAsync<InvalidDataException>(() => bank.RestorePreviousAsync());
    }

    [Fact]
    public async Task LegacyCopyMustBePromotedByRemovingActualSource()
    {
        var file = new File(); var bank = Bank();
        var pokemon = View(file.Data).Boxes[0].Slots[0];
        var copy = await bank.AddAsync(pokemon, "SS", "TEST");
        Assert.False(copy.Item.Entry.TransferEligible);
        await Assert.ThrowsAsync<InvalidDataException>(() => bank.MoveToSaveAsync(copy.Item.Entry.Id, file, new(false, 0, 1), Hash(file.Data)));
        Assert.Equal(0, file.Writes);
        await bank.MoveIntoBankAsync(file, Box, Hash(file.Data));
        var moved = Assert.Single(await bank.ListAsync());
        Assert.Equal(copy.Item.Entry.Id, moved.Entry.Id);
        Assert.True(moved.Entry.TransferEligible);
        Assert.Equal((ushort)0, View(file.Data).Boxes[0].Slots[0].Species);
    }

    [Fact]
    public async Task StalePreviewAndOccupiedDestinationNeverWrite()
    {
        var file = new File(); var bank = Bank();
        var stale = Hash(file.Data); file.Data[0x30000] ^= 1;
        await Assert.ThrowsAsync<InvalidDataException>(() => bank.MoveIntoBankAsync(file, Box, stale));
        Assert.Equal(0, file.Writes); Assert.False(await bank.HasPendingMoveAsync());
        await bank.MoveIntoBankAsync(file, Box, Hash(file.Data));
        var entry = Assert.Single(await bank.ListAsync());
        var target = new File();
        await Assert.ThrowsAsync<InvalidDataException>(() => bank.MoveToSaveAsync(entry.Entry.Id, target, Box, Hash(target.Data)));
        Assert.Equal(0, target.Writes); Assert.Single(await bank.ListAsync());
    }

    [Theory]
    [InlineData("before")]
    [InlineData("after")]
    public async Task InterruptedDepositRecoversWithoutDuplicateOwnership(string failure)
    {
        var file = new File { Failure = failure }; var before = file.Data.ToArray();
        await Assert.ThrowsAsync<IOException>(() => Bank().MoveIntoBankAsync(file, Box, Hash(file.Data)));
        Assert.True(await Bank().HasPendingMoveAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => Bank().MoveIntoBankAsync(file, Box, Hash(file.Data)));
        file.Failure = "";
        await Bank().RecoverMoveAsync(file);
        Assert.False(await Bank().HasPendingMoveAsync());
        if (failure == "before") { Assert.Empty(await Bank().ListAsync()); Assert.Equal(before, file.Data); }
        else { Assert.Single(await Bank().ListAsync()); Assert.Equal((ushort)0, View(file.Data).Boxes[0].Slots[0].Species); }
        Assert.Equal(1, file.Writes);
    }

    [Fact]
    public async Task InterruptedWithdrawalStaysLockedUntilVerifiedAndThenConsumesEntry()
    {
        var file = new File(); var bank = Bank();
        await bank.MoveIntoBankAsync(file, Box, Hash(file.Data));
        var entry = Assert.Single(await bank.ListAsync());
        var backup = await bank.ExportAsync();
        file.Failure = "after";
        await Assert.ThrowsAsync<IOException>(() => bank.MoveToSaveAsync(entry.Entry.Id, file, Box, Hash(file.Data)));
        Assert.True(await Bank().HasPendingMoveAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => Bank().ImportAsync(backup));
        file.Failure = "";
        await Bank().RecoverMoveAsync(file);
        Assert.Empty(await Bank().ListAsync());
        Assert.Equal((ushort)152, View(file.Data).Boxes[0].Slots[0].Species);
        Assert.Equal(0, await Bank().ImportAsync(backup));
    }

    [Fact]
    public async Task PartialWriteRequiresExplicitRecoveryAndRestoresExactBackup()
    {
        var file = new File { Failure = "partial" }; var original = file.Data.ToArray();
        await Assert.ThrowsAsync<IOException>(() => Bank().MoveIntoBankAsync(file, Box, Hash(original)));
        await Assert.ThrowsAsync<SaveRecoveryRequiredException>(() => Bank().RecoverMoveAsync(file));
        file.Failure = "";
        await Bank().RecoverMoveAsync(file, restoreOriginal: true);
        Assert.Equal(original, file.Data); Assert.Empty(await Bank().ListAsync());
        Assert.False(await Bank().HasPendingMoveAsync());
    }

    [Fact]
    public async Task FailedFinalBankCommitIsRecoveredFromVerifiedSave()
    {
        var file = new File();
        file.AfterWrite = () =>
        {
            var backup = Path.Combine(root, "bank.json.bak");
            if (System.IO.File.Exists(backup)) System.IO.File.Delete(backup);
            Directory.CreateDirectory(backup);
        };
        var error = await Record.ExceptionAsync(() => Bank().MoveIntoBankAsync(file, Box, Hash(file.Data)));
        Assert.True(error is IOException or UnauthorizedAccessException);
        Assert.True(await Bank().HasPendingMoveAsync());
        Assert.Equal((ushort)0, View(file.Data).Boxes[0].Slots[0].Species);
        Directory.Delete(Path.Combine(root, "bank.json.bak")); file.AfterWrite = null;
        await Bank().RecoverMoveAsync(file);
        Assert.Single(await Bank().ListAsync()); Assert.Equal(1, file.Writes);
    }

    [Fact]
    public async Task BackupFailureNeverTouchesSource()
    {
        Directory.CreateDirectory(root);
        await System.IO.File.WriteAllTextAsync(Path.Combine(root, "save-backups"), "blocked");
        var file = new File();
        await Assert.ThrowsAnyAsync<IOException>(() => Bank().MoveIntoBankAsync(file, Box, Hash(file.Data)));
        Assert.Equal(0, file.Writes);
        await Bank().RecoverMoveAsync(file);
        Assert.Empty(await Bank().ListAsync());
    }

    [Fact]
    public async Task PartyRemovalCompactsSlotsAndKeepsAUsableMember()
    {
        var file = new File(); var bank = Bank();
        await bank.MoveIntoBankAsync(file, new(true, -1, 0), Hash(file.Data));
        Assert.Equal((ushort)155, Assert.Single(View(file.Data).Party).Species);
        var before = file.Data.ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => bank.MoveIntoBankAsync(file, new(true, -1, 0), Hash(file.Data)));
        Assert.Equal(before, file.Data);
        var entry = Assert.Single(await bank.ListAsync());
        await bank.MoveToSaveAsync(entry.Entry.Id, file, new(false, 0, 1), Hash(file.Data));
        Assert.Equal((ushort)158, View(file.Data).Boxes[0].Slots[1].Species);
    }

    [Fact]
    public async Task DuplicateInDestinationAndWrongRecoveryDocumentAreRejected()
    {
        var source = new File(); var bank = Bank();
        await bank.MoveIntoBankAsync(source, Box, Hash(source.Data));
        var entry = Assert.Single(await bank.ListAsync());
        var target = new File();
        await Assert.ThrowsAsync<InvalidDataException>(() => bank.MoveToSaveAsync(entry.Entry.Id, target, new(false, 0, 1), Hash(target.Data)));
        Assert.Equal(0, target.Writes);
        source.Failure = "after";
        await Assert.ThrowsAsync<IOException>(() => bank.MoveToSaveAsync(entry.Entry.Id, source, Box, Hash(source.Data)));
        var wrong = new OtherDocument();
        await Assert.ThrowsAsync<InvalidDataException>(() => bank.RecoverMoveAsync(wrong, restoreOriginal: true));
        Assert.Equal(0, wrong.Writes);
    }

    [Fact]
    public async Task MailCannotBeMovedWithoutMailStorage()
    {
        var data = Save();
        var save = (SAV4HGSS)SaveUtil.GetSaveFile(data)!;
        var p = save.GetBoxSlotAtIndex(0, 0); p.HeldItem = 137;
        save.SetBoxSlotAtIndex(p, 0, 0, EntityImportSettings.None);
        var file = new File { Data = save.Write().ToArray() };
        await Assert.ThrowsAsync<InvalidDataException>(() => Bank().MoveIntoBankAsync(file, Box, Hash(file.Data)));
        Assert.Equal(0, file.Writes);
    }

    private sealed class OtherDocument : ISaveDocument
    {
        public string Id => "different-document";
        public int Writes;
        public Task<byte[]> ReadAsync() => Task.FromResult(Save());
        public Task WriteAsync(byte[] data) { Writes++; return Task.CompletedTask; }
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
