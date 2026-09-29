using FlashEditor;
using FlashEditor.Cache;
using FlashEditor.Definitions.Editing;
using FlashEditor.Definitions.Entities;
using FlashEditor.Definitions.Models.Interchange;
using FlashEditor.Utils;

// Stands in for FlashEditor's "New model from OBJ" (twice) and "New item from selected" on 4718,
// then the item-grid edits, then File > Save: the same public calls the UI makes
// (EntityBrowserPanel), and RSCache.WriteCache as Editor.SaveCache calls it (minus
// the JS5 reload handshake, which has no server to talk to here). The replace mode stands in for
// "Replace from OBJ (rebuild)..." the same way.
// usage: stage <cacheDir> <wornObj> <invObj>
//        stage <cacheDir> replace <modelId> <obj> [--label <n> | --no-label]
//        stage <cacheDir> item <itemId> set <name=value>...
//        stage dump <cacheDir> <itemId>
if (args.Length == 0) return Usage();
if (args[0] == "dump") { Dump(new RSCache(new RSFileStore(args[1])), int.Parse(args[2])); return 0; }
if (args.Length >= 2 && args[1] == "replace") return Replace(args);
if (args.Length >= 2 && args[1] == "item") return EditItem(args);
if (args.Length != 3) return Usage();

string dir = args[0];
var cache = new RSCache(new RSFileStore(dir));
Console.WriteLine($"next model id {cache.NextCreatableGroupId(RSConstants.MODELS_INDEX)}, next item id {NewItemFromTemplate.NextItemId(cache)}");

ModelImportResult worn = NewModelFromObj.StageFile(cache, args[1], NewModelFromObj.WeaponVertexLabel);
if (!worn.Succeeded) { Console.WriteLine("worn refused: " + worn.Message); return 1; }
ModelImportResult inv = NewModelFromObj.StageFile(cache, args[2], null);
if (!inv.Succeeded) { Console.WriteLine("inv refused: " + inv.Message); return 1; }
Console.WriteLine($"worn model {worn.Model.ModelId}: {worn.Message}");
Console.WriteLine($"inv model {inv.Model.ModelId}: {inv.Message}");

ItemDefinition item = NewItemFromTemplate.Stage(cache, 4718);
item.name = "Saradomin's Will";
item.inventoryModelId = inv.Model.ModelId;
item.maleWearModel1 = worn.Model.ModelId;
item.femaleWearModel1 = worn.Model.ModelId;
DefinitionWriter.Save(cache, RSConstants.ITEM_DEFINITIONS_INDEX, item.id, item.Encode().ToArray());
Console.WriteLine($"item {item.id} staged");

Save(cache, dir);
Console.WriteLine("saved; reopening");
var reopened = new RSCache(new RSFileStore(dir));
Dump(reopened, 4718);
Dump(reopened, item.id);
foreach (int m in new[] { worn.Model.ModelId, inv.Model.ModelId })
    Console.WriteLine($"model {m}: {reopened.ReadFileBytes(RSConstants.MODELS_INDEX, m, 0).Length} bytes on reopen");
return 0;

static int Replace(string[] args) {
    if (args.Length < 4 || !int.TryParse(args[2], out int modelId)) return Usage();

    int? label = NewModelFromObj.WeaponVertexLabel;
    for (int i = 4; i < args.Length; i++) {
        if (args[i] == "--no-label") label = null;
        else if (args[i] == "--label" && i + 1 < args.Length && int.TryParse(args[++i], out int n)) label = n;
        else return Usage();
    }

    //The store's sector-level trace would bury the four lines this mode exists to print.
    DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
    string dir = args[0];
    var cache = new RSCache(new RSFileStore(dir));
    int? oldSize = cache.GetReferenceTable(RSConstants.MODELS_INDEX).validArchiveIds.Contains(modelId)
        ? cache.ReadGroup(RSConstants.MODELS_INDEX, modelId).Values.Single().Length
        : null;

    ModelImportResult result = NewModelFromObj.ReplaceFile(cache, modelId, args[3], label);
    Console.WriteLine($"model {modelId}: old {(oldSize.HasValue ? oldSize + " bytes" : "none")}, " +
                      $"new {(result.Succeeded ? result.Model.Encode().ToArray().Length + " bytes" : "none")}, " +
                      $"vertex label {(label.HasValue ? label.ToString() : "none")}");
    Console.WriteLine(result.Message);
    if (!result.Succeeded) { Console.WriteLine("refused; nothing staged"); return 1; }
    if (!result.GeometryChanged) { Console.WriteLine("staged: no (identical bytes); nothing saved"); return 0; }

    Console.WriteLine("staged: yes");
    Save(cache, dir);
    var reopened = new RSCache(new RSFileStore(dir));
    Console.WriteLine($"saved through RSCache.WriteCache to {Path.GetFullPath(dir)}; model {modelId}: " +
                      $"{reopened.ReadGroup(RSConstants.MODELS_INDEX, modelId).Values.Single().Length} bytes on reopen");
    return 0;
}

static int EditItem(string[] args) {
    if (args.Length < 5 || !int.TryParse(args[2], out int itemId) || args[3] != "set") return Usage();

    DebugUtil.LOG_LEVEL = DebugUtil.LOG_DETAIL.NONE;
    string dir = args[0];
    var cache = new RSCache(new RSFileStore(dir));
    ItemEditOutcome outcome;
    try {
        outcome = ItemViewEdit.Apply(cache, itemId, args.Skip(4));
    }
    catch (FormatException e) {
        Console.WriteLine(e.Message);
        Console.WriteLine("refused; nothing staged");
        return 1;
    }

    Console.WriteLine($"item {itemId} before: {outcome.Before}");
    Console.WriteLine($"item {itemId} after:  {outcome.After}");
    if (!outcome.Staged) { Console.WriteLine("staged: no (identical bytes); nothing saved"); return 0; }

    Console.WriteLine("staged: yes");
    Save(cache, dir);
    var reopened = new RSCache(new RSFileStore(dir));
    Console.WriteLine($"saved through RSCache.WriteCache to {Path.GetFullPath(dir)}; on reopen: " +
                      ItemViewEdit.Describe(reopened.GetItemDefinition(itemId >> 8, itemId & 0xff)));
    return 0;
}

//Internal, and reached through the InternalsVisibleTo FlashEditor.csproj grants this assembly.
static void Save(RSCache c, string dir) => c.WriteCache(dir);

static int Usage() {
    Console.WriteLine("usage: stage <cacheDir> <wornObj> <invObj>");
    Console.WriteLine("       stage <cacheDir> replace <modelId> <obj> [--label <n> | --no-label]");
    Console.WriteLine("       stage <cacheDir> item <itemId> set <name=value>...");
    Console.WriteLine("         names: zoom xan2d yan2d zan2d xoff2d yoff2d param.<key> (value an int or a \"quoted\" string)");
    Console.WriteLine("       stage dump <cacheDir> <itemId>");
    return 2;
}

static void Dump(RSCache c, int id) {
    ItemDefinition d = c.GetItemDefinition(id >> 8, id & 0xff);
    string ops = string.Join(",", d.Opcodes.Select(o => o.Opcode));
    string ps = d.itemParams == null ? "none" : string.Join(" ", d.itemParams.Select(p => $"{p.Key}={p.Value}"));
    Console.WriteLine($"item {id} '{d.name}' inv {d.inventoryModelId} wear {d.maleWearModel1}/{d.femaleWearModel1} zoom {d.modelZoom} rot {d.modelRotation1},{d.modelRotation2} off {d.modelOffsetX},{d.modelOffsetY} note {d.notedId} lend {d.lendId} ops [{ops}] params {ps}");
}
