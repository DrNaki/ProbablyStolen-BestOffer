using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Xml.Linq;
using Steamworks;

return Bootstrap.Run(args);

static class Bootstrap
{
    public static int Run(string[] args)
    {
        // No arguments show usage. Creating a Workshop item always needs the
        // explicit create-private command and a fresh durable state file.
        if (args.Length == 0 || args[0] is "--help" or "help")
        {
            Console.WriteLine("WorkshopPublisher probe [--game-dir PATH]\nWorkshopPublisher details --id ID [--game-dir PATH]\nWorkshopPublisher create-private --config CONFIG.json\nWorkshopPublisher update --config CONFIG.json --id ID [--visibility private|public|unlisted]\nWorkshopPublisher publish --config CONFIG.json --id ID\nWorkshopPublisher add-previews --config CONFIG.json --id ID");
            return 0;
        }
        var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--") || i + 1 >= args.Length)
                throw new ArgumentException("Expected --option VALUE pairs.");
            parsed.Add(args[i][2..], args[i + 1]);
        }
        var config = parsed.TryGetValue("config", out var configPath)
            ? JsonSerializer.Deserialize<PublishConfig>(File.ReadAllText(configPath), Json.Options) ?? throw new InvalidDataException("Empty config")
            : new PublishConfig();
        var gameDir = Path.GetFullPath(parsed.GetValueOrDefault("game-dir") ?? config.GameDirectory);
        var sdkPath = Path.Combine(gameDir, "Probably Stolen_Data", "Managed", "com.rlabrecque.steamworks.net.dll");
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name == "com.rlabrecque.steamworks.net"
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(sdkPath) : null;
        try { return Publisher.Run(args[0], parsed, config, gameDir); }
        catch (Exception error)
        {
            Json.Event(new { Event = "error", Error = error.ToString() });
            return 1;
        }
    }
}

sealed class PublishConfig
{
    public string GameDirectory { get; set; } = @"E:\Steam\steamapps\common\Probably Stolen";
    public string ContentFolder { get; set; } = "";
    public string PreviewFile { get; set; } = "";
    public string[] AdditionalPreviewFiles { get; set; } = [];
    public string Title { get; set; } = "最佳报价 / Best Offer - Smart Trading Assistant";
    public string DescriptionFile { get; set; } = "";
    public string Language { get; set; } = "english";
    public string ChangeNote { get; set; } = "Steam Workshop release of Best Offer 1.6.1.";
    public string[] Tags { get; set; } = ["Mod", "Quality of Life", "UI"];
    public string StateFile { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 300;
}

sealed class PublishState
{
    public uint AppId { get; set; } = Publisher.AppId;
    public ulong OwnerSteamId { get; set; }
    public ulong PublishedFileId { get; set; }
    public DateTimeOffset? CreationAttemptedUtc { get; set; }
    public DateTimeOffset? CreatedUtc { get; set; }
    public DateTimeOffset? UpdatedUtc { get; set; }
    public string Stage { get; set; } = "prepared";
    public string? Result { get; set; }
    public bool IoFailure { get; set; }
    public bool NeedsLegalAgreement { get; set; }
    public string? Visibility { get; set; }
    public string? Error { get; set; }
    public List<UploadedPreview> AdditionalPreviews { get; set; } = [];
    public PreviewOperation? PendingPreviewOperation { get; set; }
    public string? LastCompletedPreviewOperationToken { get; set; }
    public string? WorkshopUrl => PublishedFileId == 0 ? null : $"https://steamcommunity.com/sharedfiles/filedetails/?id={PublishedFileId}";
}

sealed class UploadedPreview
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public uint Index { get; set; }
    public DateTimeOffset UploadedUtc { get; set; }
}

sealed class PreviewChange
{
    public string Action { get; set; } = "add";
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public uint Index { get; set; }
}

sealed class PreviewOperation
{
    public string Token { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset RequestedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Stage { get; set; } = "submit-requested";
    public string? Result { get; set; }
    public bool IoFailure { get; set; }
    public List<PreviewChange> Changes { get; set; } = [];
}

sealed record RemotePreview(uint Index, string FileName, string UrlOrVideoId, EItemPreviewType Type);

static class Json
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public static void Event(object value) { Console.WriteLine(JsonSerializer.Serialize(value, Options)); Console.Out.Flush(); }
    public static void Save(string path, PublishState state)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".new";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, Options), new UTF8Encoding(false));
        File.Move(temp, path, true);
    }
}

static class Publisher
{
    public const uint AppId = 4348910;
    public static int Run(string command, Dictionary<string, string> options, PublishConfig config, string gameDir)
    {
        if (command is not ("probe" or "details" or "create-private" or "update" or "publish" or "add-previews"))
            throw new ArgumentException("Unknown command: " + command);
        var content = command is "probe" or "details" or "add-previews" ? null : Validate(config);
        if (command == "add-previews") ValidateAdditionalPreviews(config, requireAny: true);
        Environment.SetEnvironmentVariable("SteamAppId", AppId.ToString());
        Environment.SetEnvironmentVariable("SteamGameId", AppId.ToString());
        var native = Path.Combine(gameDir, "Probably Stolen_Data", "Plugins", "x86_64", "steam_api64.dll");
        NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly,
            (name, _, _) => name.Contains("steam_api", StringComparison.OrdinalIgnoreCase) ? NativeLibrary.Load(native) : IntPtr.Zero);
        if (!SteamAPI.Init()) throw new InvalidOperationException("SteamAPI.Init failed. Start the logged-in Steam client that owns Probably Stolen.");
        try
        {
            var owner = SteamUser.GetSteamID().m_SteamID;
            var actualApp = SteamUtils.GetAppID().m_AppId;
            var ownsGame = SteamApps.BIsSubscribedApp(new AppId_t(AppId));
            Json.Event(new { Event = "steam-ready", Initialized = true, LoggedOn = SteamUser.BLoggedOn(), AppId = actualApp, OwnerSteamId = owner, OwnsGame = ownsGame });
            if (command == "probe") return 0;
            if (!SteamUser.BLoggedOn() || actualApp != AppId || !ownsGame)
                throw new InvalidOperationException("The logged-in account must own App 4348910 and the API context must match it.");
            if (command == "details") return Details(ParseId(options), config.TimeoutSeconds, options.GetValueOrDefault("language") ?? config.Language);
            var statePath = Path.GetFullPath(config.StateFile);
            var state = File.Exists(statePath)
                ? JsonSerializer.Deserialize<PublishState>(File.ReadAllText(statePath), Json.Options) ?? throw new InvalidDataException("Unreadable state file")
                : new PublishState { OwnerSteamId = owner };
            if (state.AppId != AppId || state.OwnerSteamId != owner)
                throw new InvalidOperationException("State file belongs to a different app or Steam account.");
            ulong itemId;
            if (command == "create-private")
            {
                if (state.PublishedFileId != 0 || state.CreationAttemptedUtc != null)
                    throw new InvalidOperationException("Creation was already attempted with this state file. Use update with its ID. If the earlier callback was unknown, inspect your existing Workshop items before any further creation.");
                state.CreationAttemptedUtc = DateTimeOffset.UtcNow;
                state.Stage = "create-requested";
                Json.Save(statePath, state); // Durable duplicate-prevention before the request.
                var created = WaitResult<CreateItemResult_t>(SteamUGC.CreateItem(new AppId_t(AppId), EWorkshopFileType.k_EWorkshopFileTypeCommunity), config.TimeoutSeconds);
                state.PublishedFileId = created.Data.m_nPublishedFileId.m_PublishedFileId;
                state.NeedsLegalAgreement = created.Data.m_bUserNeedsToAcceptWorkshopLegalAgreement;
                state.Result = created.Data.m_eResult.ToString();
                state.IoFailure = created.IoFailure;
                state.Stage = "create-callback";
                state.CreatedUtc = DateTimeOffset.UtcNow;
                Json.Save(statePath, state); // Save the server ID before any metadata/content update.
                Json.Event(new { Event = "create-callback", state.PublishedFileId, Result = state.Result, created.IoFailure, state.NeedsLegalAgreement, state.WorkshopUrl });
                if (created.IoFailure || created.Data.m_eResult != EResult.k_EResultOK || state.PublishedFileId == 0)
                    throw new InvalidOperationException("CreateItem failed; durable state preserves the callback.");
                itemId = state.PublishedFileId;
            }
            else
            {
                itemId = ParseId(options);
                if (state.PublishedFileId != 0 && state.PublishedFileId != itemId)
                    throw new InvalidOperationException("Explicit ID differs from durable state. Refusing to update another item.");
                state.PublishedFileId = itemId;
            }
            var visibility = command == "publish" ? "public" : command == "create-private" ? "private" : options.GetValueOrDefault("visibility") ?? "private";
            try { return command == "add-previews"
                    ? UpdatePreviews(itemId, config, state, statePath)
                    : Update(itemId, visibility, config, content!, state, statePath); }
            catch (Exception error)
            {
                state.Error = error.Message;
                Json.Save(statePath, state);
                throw;
            }
        }
        finally { SteamAPI.Shutdown(); }
    }

    static ulong ParseId(Dictionary<string, string> options) => options.TryGetValue("id", out var text)
        && ulong.TryParse(text, out var id) && id > 0 ? id : throw new ArgumentException("An explicit --id is required.");

    static string Validate(PublishConfig config)
    {
        CheckLanguage(config.Language);
        ValidateAdditionalPreviews(config);
        config.ContentFolder = Path.GetFullPath(config.ContentFolder);
        config.PreviewFile = Path.GetFullPath(config.PreviewFile);
        if (Path.GetFullPath(config.StateFile).StartsWith(config.ContentFolder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("StateFile must be outside ContentFolder so publication metadata is never uploaded as mod content.");
        var manifest = XDocument.Load(Path.Combine(config.ContentFolder, "manifest.xml"));
        if (manifest.Root?.Element("ID")?.Value.Trim() != "naki.bestoffer")
            throw new InvalidDataException("Only the naki.bestoffer package is accepted by this publisher.");
        var dlls = manifest.Root.Element("Assemblies")?.Elements("Assembly").Select(x => x.Value.Trim()).ToArray();
        if (dlls == null || dlls.Length != 1 || dlls[0] != "BestOffer.dll" || !File.Exists(Path.Combine(config.ContentFolder, "BestOffer.dll")))
            throw new InvalidDataException("The package must list only BestOffer.dll.");
        if (Directory.EnumerateFiles(config.ContentFolder, "*", SearchOption.AllDirectories).Any(p => Path.GetFileName(p).Contains("WarehouseSort", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("WarehouseSort content is outside this release scope.");
        if (new FileInfo(config.PreviewFile).Length >= 1024 * 1024) throw new InvalidDataException("Preview must be smaller than 1 MiB.");
        var description = File.ReadAllText(config.DescriptionFile, Encoding.UTF8);
        CheckLength(config.Title, Constants.k_cchPublishedDocumentTitleMax - 1, "Title");
        CheckLength(description, Constants.k_cchPublishedDocumentDescriptionMax - 1, "Description");
        CheckLength(config.ChangeNote, Constants.k_cchPublishedDocumentChangeDescriptionMax - 1, "ChangeNote");
        CheckLength(string.Join(',', config.Tags), Constants.k_cchTagListMax - 1, "Tags");
        return description;
    }

    static void ValidateAdditionalPreviews(PublishConfig config, bool requireAny = false)
    {
        if (string.IsNullOrWhiteSpace(config.StateFile)) throw new InvalidDataException("StateFile is required.");
        if (config.TimeoutSeconds < 10 || config.TimeoutSeconds > 1800) throw new InvalidDataException("TimeoutSeconds must be 10..1800.");
        config.AdditionalPreviewFiles ??= [];
        if (requireAny && config.AdditionalPreviewFiles.Length == 0)
            throw new InvalidDataException("add-previews requires at least one AdditionalPreviewFiles entry.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < config.AdditionalPreviewFiles.Length; i++)
        {
            var path = Path.GetFullPath(config.AdditionalPreviewFiles[i]);
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is not (".png" or ".jpg" or ".jpeg"))
                throw new InvalidDataException("Additional previews must be PNG or JPEG: " + path);
            var file = new FileInfo(path);
            if (!file.Exists || file.Length == 0 || file.Length >= 1024 * 1024)
                throw new InvalidDataException("Each additional preview must exist and be 1..1048575 bytes: " + path);
            if (!names.Add(file.Name)) throw new InvalidDataException("Additional preview file names must be unique: " + file.Name);
            config.AdditionalPreviewFiles[i] = path;
        }
    }

    static void CheckLength(string value, int max, string name)
    {
        var length = Encoding.UTF8.GetByteCount(value);
        if (length == 0 || length > max) throw new InvalidDataException($"{name} is {length} UTF-8 bytes; expected 1..{max}. No truncation was applied.");
    }

    static int Update(ulong id, string visibility, PublishConfig config, string description, PublishState state, string statePath)
    {
        var previewChanges = PlanPreviewChanges(id, config, state);
        var view = visibility.ToLowerInvariant() switch
        {
            "private" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPrivate,
            "public" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityPublic,
            "unlisted" => ERemoteStoragePublishedFileVisibility.k_ERemoteStoragePublishedFileVisibilityUnlisted,
            _ => throw new ArgumentException("Unknown visibility: " + visibility)
        };
        var handle = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(id));
        if (handle == UGCUpdateHandle_t.Invalid) throw new InvalidOperationException("StartItemUpdate returned Invalid.");
        Set(SteamUGC.SetItemUpdateLanguage(handle, config.Language), "title/description language");
        Set(SteamUGC.SetItemTitle(handle, config.Title), "title");
        Set(SteamUGC.SetItemDescription(handle, description), "description");
        Set(SteamUGC.SetItemContent(handle, config.ContentFolder), "content");
        Set(SteamUGC.SetItemPreview(handle, config.PreviewFile), "preview");
        Set(SteamUGC.SetItemTags(handle, config.Tags), "tags");
        Set(SteamUGC.SetItemVisibility(handle, view), "visibility");
        StagePreviewChanges(handle, previewChanges, state);
        state.Stage = "submit-requested";
        state.Visibility = visibility;
        state.Error = null;
        Json.Save(statePath, state);
        var submitted = WaitResult<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(handle, config.ChangeNote), config.TimeoutSeconds, () =>
        {
            var progress = SteamUGC.GetItemUpdateProgress(handle, out var done, out var total);
            Json.Event(new { Event = "upload-progress", Status = progress.ToString(), Processed = done, Total = total });
        });
        state.Stage = "submit-callback";
        state.Result = submitted.Data.m_eResult.ToString();
        state.IoFailure = submitted.IoFailure;
        state.NeedsLegalAgreement |= submitted.Data.m_bUserNeedsToAcceptWorkshopLegalAgreement;
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        FinishPreviewChanges(state, submitted.Data.m_eResult, submitted.IoFailure);
        Json.Save(statePath, state);
        Json.Event(new { Event = "submit-callback", PublishedFileId = submitted.Data.m_nPublishedFileId.m_PublishedFileId, Result = state.Result, submitted.IoFailure, state.NeedsLegalAgreement, Visibility = visibility, state.WorkshopUrl });
        return !submitted.IoFailure && submitted.Data.m_eResult == EResult.k_EResultOK ? 0 : 1;
    }

    static int UpdatePreviews(ulong id, PublishConfig config, PublishState state, string statePath)
    {
        var changes = PlanPreviewChanges(id, config, state);
        if (changes.Count == 0)
        {
            Json.Event(new { Event = "previews-unchanged", PublishedFileId = id, Note = "All configured image digests already match uploaded previews. No Steam update was submitted." });
            return 0;
        }
        var handle = SteamUGC.StartItemUpdate(new AppId_t(AppId), new PublishedFileId_t(id));
        if (handle == UGCUpdateHandle_t.Invalid) throw new InvalidOperationException("StartItemUpdate returned Invalid.");
        // Deliberately no SetItemVisibility, SetItemContent, SetItemPreview,
        // SetItemTitle, SetItemDescription or tags in this command.
        StagePreviewChanges(handle, changes, state);
        state.Stage = "previews-submit-requested";
        state.Error = null;
        Json.Save(statePath, state); // Durable operation token before submitting.
        var submitted = WaitResult<SubmitItemUpdateResult_t>(SteamUGC.SubmitItemUpdate(handle, ""), config.TimeoutSeconds, () =>
        {
            var progress = SteamUGC.GetItemUpdateProgress(handle, out var done, out var total);
            Json.Event(new { Event = "preview-upload-progress", Status = progress.ToString(), Processed = done, Total = total });
        });
        state.Stage = "previews-submit-callback";
        state.Result = submitted.Data.m_eResult.ToString();
        state.IoFailure = submitted.IoFailure;
        state.NeedsLegalAgreement |= submitted.Data.m_bUserNeedsToAcceptWorkshopLegalAgreement;
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        FinishPreviewChanges(state, submitted.Data.m_eResult, submitted.IoFailure);
        Json.Save(statePath, state);
        Json.Event(new { Event = "previews-submit-callback", PublishedFileId = id, Result = state.Result, submitted.IoFailure,
            state.NeedsLegalAgreement, state.LastCompletedPreviewOperationToken, AdditionalPreviews = state.AdditionalPreviews, state.WorkshopUrl });
        return !submitted.IoFailure && submitted.Data.m_eResult == EResult.k_EResultOK ? 0 : 1;
    }

    static List<PreviewChange> PlanPreviewChanges(ulong id, PublishConfig config, PublishState state)
    {
        if (state.PendingPreviewOperation != null)
            throw new InvalidOperationException("A prior preview operation has no confirmed success: " + state.PendingPreviewOperation.Token
                + ". Do not retry blindly. Run details, inspect the remote filenames/indexes and the saved operation before resolving its state.");
        var plan = new List<PreviewChange>();
        if (config.AdditionalPreviewFiles.Length == 0) return plan;
        var remote = ReadRemotePreviews(id, config.TimeoutSeconds, state.OwnerSteamId);
        var nextIndex = (uint)remote.Count;
        var seenDigests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in config.AdditionalPreviewFiles)
        {
            var name = Path.GetFileName(path);
            var digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
            if (!seenDigests.Add(digest)) continue; // Same image under two local names.
            var identical = state.AdditionalPreviews.FirstOrDefault(p => p.Sha256.Equals(digest, StringComparison.OrdinalIgnoreCase)
                && remote.Any(r => r.Type == EItemPreviewType.k_EItemPreviewType_Image
                    && r.FileName.Equals(p.FileName, StringComparison.OrdinalIgnoreCase)));
            if (identical != null) continue;
            var matching = remote.Where(p => p.Type == EItemPreviewType.k_EItemPreviewType_Image
                && p.FileName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matching.Count > 1) throw new InvalidOperationException("Remote preview filename is ambiguous; inspect details before updating: " + name);
            plan.Add(new PreviewChange { Action = matching.Count == 1 ? "update" : "add", FilePath = path, FileName = name,
                Sha256 = digest, Index = matching.Count == 1 ? matching[0].Index : nextIndex++ });
        }
        Json.Event(new { Event = "preview-plan", PublishedFileId = id, ExistingCount = remote.Count, Changes = plan });
        return plan;
    }

    static void StagePreviewChanges(UGCUpdateHandle_t handle, List<PreviewChange> changes, PublishState state)
    {
        if (changes.Count == 0) return;
        foreach (var change in changes)
        {
            var accepted = change.Action == "update"
                ? SteamUGC.UpdateItemPreviewFile(handle, change.Index, change.FilePath)
                : SteamUGC.AddItemPreviewFile(handle, change.FilePath, EItemPreviewType.k_EItemPreviewType_Image);
            Set(accepted, "additional preview " + change.FileName);
        }
        state.PendingPreviewOperation = new PreviewOperation { Changes = changes };
    }

    static void FinishPreviewChanges(PublishState state, EResult result, bool ioFailure)
    {
        var pending = state.PendingPreviewOperation;
        if (pending == null) return;
        pending.Result = result.ToString();
        pending.IoFailure = ioFailure;
        pending.Stage = "submit-callback";
        if (ioFailure || result != EResult.k_EResultOK) return; // Retain ambiguous/failed request to prevent accidental re-addition.
        foreach (var change in pending.Changes)
        {
            state.AdditionalPreviews.RemoveAll(p => p.Index == change.Index || p.FileName.Equals(change.FileName, StringComparison.OrdinalIgnoreCase));
            state.AdditionalPreviews.Add(new UploadedPreview { FilePath = change.FilePath, FileName = change.FileName,
                Sha256 = change.Sha256, Index = change.Index, UploadedUtc = DateTimeOffset.UtcNow });
        }
        state.LastCompletedPreviewOperationToken = pending.Token;
        state.PendingPreviewOperation = null;
    }

    static List<RemotePreview> ReadRemotePreviews(ulong id, int timeout, ulong expectedOwner)
    {
        var query = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(id)], 1);
        if (query == UGCQueryHandle_t.Invalid) throw new InvalidOperationException("Preview query handle Invalid.");
        try
        {
            Set(SteamUGC.SetReturnAdditionalPreviews(query, true), "query additional previews");
            Set(SteamUGC.SetAllowCachedResponse(query, 0), "query fresh previews");
            var result = WaitResult<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query), timeout);
            if (result.IoFailure || result.Data.m_eResult != EResult.k_EResultOK || !SteamUGC.GetQueryUGCResult(query, 0, out var item)
                || item.m_eResult != EResult.k_EResultOK)
                throw new InvalidOperationException("Could not read the item's existing previews. Nothing was submitted.");
            if (item.m_ulSteamIDOwner != expectedOwner || item.m_nConsumerAppID.m_AppId != AppId)
                throw new InvalidOperationException("Refusing to change previews for another owner or app.");
            return QueryPreviews(query);
        }
        finally { SteamUGC.ReleaseQueryUGCRequest(query); }
    }

    static List<RemotePreview> QueryPreviews(UGCQueryHandle_t query)
    {
        var list = new List<RemotePreview>();
        var count = SteamUGC.GetQueryUGCNumAdditionalPreviews(query, 0);
        for (uint index = 0; index < count; index++)
        {
            if (!SteamUGC.GetQueryUGCAdditionalPreview(query, 0, index, out var url, 4096, out var name, 4096, out var type))
                throw new InvalidOperationException("Could not read additional preview index " + index);
            list.Add(new RemotePreview(index, Path.GetFileName(name ?? ""), url ?? "", type));
        }
        return list;
    }

    static void Set(bool success, string field) { if (!success) throw new InvalidOperationException("Steam rejected SetItem " + field); }

    static (T Data, bool IoFailure) WaitResult<T>(SteamAPICall_t call, int timeout, Action? progress = null) where T : struct
    {
        if (call == SteamAPICall_t.Invalid) throw new InvalidOperationException("Steam returned an invalid API call handle.");
        var complete = false;
        T data = default;
        var ioFailure = false;
        using var result = CallResult<T>.Create((value, failure) => { data = value; ioFailure = failure; complete = true; });
        result.Set(call);
        var deadline = DateTime.UtcNow.AddSeconds(timeout);
        var nextProgress = DateTime.MinValue;
        while (!complete)
        {
            SteamAPI.RunCallbacks();
            if (complete) break;
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Steam callback timed out. Do not repeat a creation request; inspect durable state and the account's Workshop items.");
            if (progress != null && DateTime.UtcNow >= nextProgress) { progress(); nextProgress = DateTime.UtcNow.AddSeconds(5); }
            Thread.Sleep(50);
        }
        return (data, ioFailure);
    }

    static void CheckLanguage(string language)
    {
        if (language is not ("english" or "schinese"))
            throw new ArgumentException("Best Offer description language must be english or schinese.");
    }

    static int Details(ulong id, int timeout, string language)
    {
        CheckLanguage(language);
        var query = SteamUGC.CreateQueryUGCDetailsRequest([new PublishedFileId_t(id)], 1);
        if (query == UGCQueryHandle_t.Invalid) throw new InvalidOperationException("Query handle Invalid.");
        try
        {
            Set(SteamUGC.SetLanguage(query, language), "query language");
            Set(SteamUGC.SetReturnLongDescription(query, true), "return long description");
            Set(SteamUGC.SetReturnAdditionalPreviews(query, true), "return additional previews");
            Set(SteamUGC.SetAllowCachedResponse(query, 0), "query fresh details");
            var result = WaitResult<SteamUGCQueryCompleted_t>(SteamUGC.SendQueryUGCRequest(query), timeout);
            if (result.IoFailure || result.Data.m_eResult != EResult.k_EResultOK || !SteamUGC.GetQueryUGCResult(query, 0, out var item))
                throw new InvalidOperationException("Details query failed: " + result.Data.m_eResult);
            SteamUGC.GetQueryUGCPreviewURL(query, 0, out var preview, 4096);
            Json.Event(new
            {
                Event = "item-details", Language = language, PublishedFileId = item.m_nPublishedFileId.m_PublishedFileId,
                Result = item.m_eResult.ToString(), OwnerSteamId = item.m_ulSteamIDOwner,
                CreatorAppId = item.m_nCreatorAppID.m_AppId, ConsumerAppId = item.m_nConsumerAppID.m_AppId,
                Title = item.m_rgchTitle, Description = item.m_rgchDescription, Visibility = item.m_eVisibility.ToString(),
                FileSize = item.m_nFileSize, PreviewFileSize = item.m_nPreviewFileSize, PreviewUrl = preview,
                Tags = item.m_rgchTags, Banned = item.m_bBanned, AcceptedForUse = item.m_bAcceptedForUse,
                UpdatedUnixTime = item.m_rtimeUpdated,
                AdditionalPreviews = QueryPreviews(query)
            });
            return 0;
        }
        finally { SteamUGC.ReleaseQueryUGCRequest(query); }
    }
}
