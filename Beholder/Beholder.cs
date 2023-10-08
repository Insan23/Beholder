using System.IO.Streams;
using Microsoft.Xna.Framework;
using Terraria;
using TerrariaApi.Server;
using TShockAPI;
using TShockAPI.Hooks;

namespace Beholder;

[ApiVersion(2, 1)]
public class Beholder : TerrariaPlugin
{
    public override string Author => "avlensa";
    public override string Name => "Beholder";
    public override Version Version => new(0, 1, 0, 0);
    public override string Description => "Look out! Cheater Detected!!!.";

    private PlayerStatus[] listPlayer;
    private Config _config;

    public enum InventoryAction
    {
        Inventory,
        Chest,
        Drop,
        Pick
    }

    public Beholder(Main game) : base(game)
    {
    }

    public override void Initialize()
    {
        ServerApi.Hooks.NetGetData.Register(this, OnGetData);
        GetDataHandlers.PlayerSlot += HandlePlayerSlot;
        GetDataHandlers.ChestOpen += HandleChestOpen;
        GetDataHandlers.ChestItemChange += HandleChestItemChange;
        GetDataHandlers.ItemDrop += HandleItemDrop;
        ServerApi.Hooks.GamePostInitialize.Register(this, OnPostInit);
        ServerApi.Hooks.ServerLeave.Register(this, OnLeave);
        PlayerHooks.PlayerPostLogin += OnPostLogin;
        GeneralHooks.ReloadEvent += OnReload;

        _config = new Config();
        _config.ReadConfig();
    }

    private void OnPostInit(EventArgs args) =>
        listPlayer = new PlayerStatus[TShock.Config.Settings.MaxSlots + TShock.Config.Settings.ReservedSlots];

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ServerApi.Hooks.NetGetData.Deregister(this, OnGetData);
            GetDataHandlers.PlayerSlot -= HandlePlayerSlot;
            GetDataHandlers.ChestOpen -= HandleChestOpen;
            GetDataHandlers.ChestItemChange -= HandleChestItemChange;
            GetDataHandlers.ItemDrop -= HandleItemDrop;
            ServerApi.Hooks.GamePostInitialize.Deregister(this, OnPostInit);
            ServerApi.Hooks.ServerLeave.Deregister(this, OnLeave);
            GeneralHooks.ReloadEvent -= OnReload;
            PlayerHooks.PlayerPostLogin -= OnPostLogin;
        }

        base.Dispose(disposing);
    }

    private void OnReload(ReloadEventArgs args)
    {
        _config.ReadConfig();
        args.Player.SendInfoMessage("Beholder configuration reloaded.");
    }

    private void OnLeave(LeaveEventArgs args) => listPlayer[args.Who] = null;

    private void OnPostLogin(PlayerPostLoginEventArgs args) => listPlayer[args.Player.Index] = new PlayerStatus();

    //todo for free craft check
    private void HandlePlayerSlot(object? sender, GetDataHandlers.PlayerSlotEventArgs args)
    {
        if (!args.Player.IsLoggedIn) return;
        if (args.Player.HasPermission("beholder.bypass")) return;
        if (IsExcluded(args.Type)) return;
        if (listPlayer[args.Player.Index] == null) return;
        var name = TShock.Utils.GetItemById(args.Type).Name;
        var invName = GetInvName(args.Slot);
        listPlayer[args.Player.Index].HandleAction(InventoryAction.Inventory, args.Slot, name, args.Stack);
        if (args.Stack >= _config.StackCheckThreshold)
            Notify($"<{args.Player.Name}> have {args.Stack} {name} in [{invName}]");
    }

    private void HandleChestOpen(object? sender, GetDataHandlers.ChestOpenEventArgs args) =>
        listPlayer[args.Player.Index].OnHandleChestOpen(args);

    private void HandleChestItemChange(object? sender, GetDataHandlers.ChestItemEventArgs args)
    {
        if (!args.Player.IsLoggedIn) return;
        if (args.Player.HasPermission("beholder.bypass")) return;
        if (IsExcluded(args.Type)) return;

        var itemName = TShock.Utils.GetItemById(args.Type).Name;
        listPlayer[args.Player.Index].HandleAction(InventoryAction.Chest, args.Slot, itemName, args.Stacks);
        if (args.Stacks >= _config.StackCheckThreshold)
            Notify($"<{args.Player.Name}> Putting {args.Stacks} {itemName} into [Chest] " +
                   $"at {listPlayer[args.Player.Index].ChestX} : {listPlayer[args.Player.Index].ChestY}");
    }

    private void HandleItemDrop(object? sender, GetDataHandlers.ItemDropEventArgs args)
    {
        if (!args.Player.IsLoggedIn) return;
        if (args.Player.HasPermission("beholder.bypass")) return;
        if (args.Stacks <= _config.StackCheckThreshold) return;
        if (IsExcluded(args.Type)) return;

        var name = TShock.Utils.GetItemById(args.Type).Name;
        if (args.Velocity.X == 0 && args.Velocity.Y == 0)
        {
            Notify($"<{args.Player.Name}> Taking {args.Stacks} {name} [from the ground]");
            listPlayer[args.Player.Index].HandleAction(InventoryAction.Pick, 0, name, args.Stacks);
        }
        else
        {
            Notify($"<{args.Player.Name}> Dropped {args.Stacks} {name} [to the ground]");
            listPlayer[args.Player.Index].HandleAction(InventoryAction.Drop, 0, name, args.Stacks);
            // Item i = new Item();
            // i._nameOverride = name;
            // i.stack = stack;
            // i.prefix = args.Prefix;
            // listPlayer[args.Player.Index].ItemPickupHistory.Add(i);
        }
    }


    public void OnGetData(GetDataEventArgs args)
    {
        if (args.MsgID != PacketTypes.ChestOpen) return;
        using var data = new MemoryStream(args.Msg.readBuffer, args.Index, args.Length);
        var id = data.ReadInt16(); //chest ID
        var x = data.ReadInt16(); //chest x
        var y = data.ReadInt16(); //chest y
        var nameLen = data.ReadInt8(); //chest name length
        if (nameLen is > 0 and <= 20) data.ReadString();

        if (id == -1) listPlayer[args.Msg.whoAmI].OnHandleChestClose();
    }

    private void Notify(string msg)
    {
        if (_config.NotifyToOnlineAdmin && _config.NotifyToConsoleLogs && _config.NotifyAllPlayer)
            TShock.Utils.Broadcast("Beholder: " + msg, (byte)TShock.Config.Settings.BroadcastRGB[0],
                (byte)TShock.Config.Settings.BroadcastRGB[1], (byte)TShock.Config.Settings.BroadcastRGB[2]);
        else
        {
            if (_config.NotifyToOnlineAdmin)
            {
                var listAdmin = new List<TSPlayer>();
                foreach (var player in TShock.Players)
                {
                    if (player == null || player.TPlayer.whoAmI < 0) continue;
                    foreach (var groupName in _config.ListAdminGroupName)
                        if (player.Group.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase))
                            listAdmin.Add(player);
                }

                listAdmin.ForEach(p => p.SendInfoMessage("Beholder: " + msg));
                var packedColor = (int)new Color(255, 255, 0).packedValue;
                listAdmin.ForEach(a => a.SendData(PacketTypes.CreateCombatTextExtended,
                    "Looks like someone is cheating",
                    packedColor, a.X, a.Y));
            }

            if (_config.NotifyAllPlayer) TSPlayer.All.SendInfoMessage("Beholder: " + msg);

            if (_config.NotifyToConsoleLogs)
                TShock.Utils.SendLogs("Beholder: " + msg,
                    new Color(TShock.Config.Settings.BroadcastRGB[0], TShock.Config.Settings.BroadcastRGB[1],
                        TShock.Config.Settings.BroadcastRGB[2]));
        }

        if (_config.SaveToLogFile) _config.AppendLogs("Beholder: " + msg);
    }

    private static string GetInvName(int slot)
    {
        return slot switch
        {
            > -1 and <= 49 => "inventory",
            > 49 and <= 53 => "coin",
            > 53 and <= 57 => "ammo",
            58 => "hand",
            > 58 and <= 61 => "armor",
            > 61 and <= 68 => "accessory",
            > 68 and <= 72 => "social armor",
            > 72 and <= 78 => "social accessory",
            > 78 and <= 81 => "dye armor",
            > 81 and <= 88 => "dye accessory",
            > 88 and <= 93 => "equipment",
            > 93 and <= 98 => "equipment dye",
            > 98 and <= 138 => "piggy bank",
            > 138 and <= 178 => "safe",
            179 => "trash",
            > 179 and <= 219 => "defender's forge",
            > 219 and <= 259 => "void bag/void vault",
            > 259 and <= 262 => "armor (loadout 1)",
            > 262 and <= 269 => "accessory (loadout 1)",
            > 269 and <= 272 => "social armor (loadout 1)",
            > 272 and <= 279 => "social accessory (loadout 1)",
            > 279 and <= 282 => "dye armor (loadout 1)",
            > 282 and <= 289 => "dye accessory (loadout 1)",
            > 289 and <= 292 => "armor (loadout 2)",
            > 292 and <= 299 => "accessory (loadout 2)",
            > 299 and <= 302 => "social armor (loadout 2)",
            > 302 and <= 309 => "social accessory (loadout 2)",
            > 309 and <= 312 => "dye armor (loadout 2)",
            > 312 and <= 319 => "dye accessory (loadout 2)",
            > 319 and <= 322 => "armor (loadout 3)",
            > 322 and <= 329 => "accessory (loadout 3)",
            > 329 and <= 332 => "social armor (loadout 3)",
            > 332 and <= 339 => "social accessory (loadout 3)",
            > 339 and <= 342 => "dye armor (loadout 3)",
            > 342 and <= 349 => "dye accessory (loadout 3)",
            _ => $"unknown {slot}"
        };
    }

    private bool IsExcluded(int Id)
    {
        foreach (var id in _config.ExcludedItemId)
            if (Id == id)
                return true;
        return false;
    }

    public class PlayerStatus
    {
        //todo used for checking free craft hack
        public bool IsOpeningChest { get; set; }

        public int ChestX { get; private set; }
        public int ChestY { get; private set; }
        public string Action1 { get; set; }
        public string Action2 { get; set; }
        public string Action3 { get; set; }
        public string Action4 { get; set; }
        private byte NextAction = 1;

        //todo change to inventory action history, probably using array pop push and inner timer to delete recent action
        //for bulk trashing checks
        // public List<string> ActionHistory { get; set; } = new();

        public void OnHandleChestOpen(GetDataHandlers.ChestOpenEventArgs args)
        {
            if (args.Player.HasPermission("beholder.bypass")) return;
            IsOpeningChest = true;
            ChestX = args.X;
            ChestY = args.Y;
        }

        public void OnHandleChestClose()
        {
            IsOpeningChest = false;
            ChestX = -1;
            ChestY = -1;
        }

        public void HandleAction(InventoryAction inv, int slot, string itemName, short stack)
        {
            //TODO next
            //todo check where the item is dropped, moved to chest, or other
            //if coin + item in hand = buy from npc
            //if inv + hand, then drop + hand = drop
            //if inv + hand, then hand + inv = move pos
            //if inv+ hand, then hand + chest = move chest


            // chest > hand > inv > hand (mobile)
            // chest > inv (pc, shift klik)
            //
            // memindah item slot
            // inv > hand > inv > hand
            //
            // inv > hand > drop > hand (drop item)
            // pick > inv (pickup)
            //
            // inv > hand > trash > hand
            //
            // inv > trash (pc, ctrl klik)

            // switch (NextAction)
            // {
            //     case 1:
            //         Action1 = $"{inv};{slot};{itemName};{stack}";
            //         NextAction++;
            //         break;
            //     case 2:
            //         Action2 = $"{inv};{slot};{itemName};{stack}";
            //         if (Action1.Split(';')[0] == InventoryAction.Inventory.ToString()
            //             && Action2.Split(';')[0] == InventoryAction.Chest.ToString())
            //         {
            //             var action = $"Moving {itemName}({stack} from {GetInvName(slot)} to Chest)";
            //         }
            //         NextAction++;
            //         break;
            //     case 3:
            //         Action3 = $"{inv};{slot};{itemName};{stack}";
            //         NextAction++;
            //         break;
            //     default:
            //         Action4 = $"{inv};{slot};{itemName};{stack}";
            //         NextAction = 0;
            //         break;
            // }
        }
    }
}