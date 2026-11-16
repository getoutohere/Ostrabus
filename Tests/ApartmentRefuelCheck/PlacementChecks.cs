using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Ostrabus.ApartmentRefuel;
using UnityEngine;

static partial class Program {
    static void PlacementChecks() {
        World();
        Ostranauts.Inventory.GUIInventory.instance = Empty<Ostranauts.Inventory.GUIInventory>();
        var host = MakeShip("OKLG", true);
        var home = MakeShip("OKLG|RES_1", true, "ResOKLG01"); Owners[home.strRegID] = "player";
        home.json.strName = home.strRegID; // Native saved residence, no retained template metadata.
        var neighbor = MakeShip("O-NEIGHBOR"); Link(home, neighbor);
        Field(neighbor, "aDocksys", new List<CondOwner>());
        var co = Owner("fit-kiosk", Runtime.Item);
        var item = Empty<Item>(); Field(item, "_co", co);
        item.nWidthInTiles = 2; item.nHeightInTiles = 2;
        var def = Read<JsonItemDef>(Assets + "/data/items/apartment_refuel.json").Single(i => i.strName == Runtime.Item);
        item.aSocketReqs = def.aSocketReqs.Select(DataHandler.GetLoot).ToList();
        item.aSocketForbids = def.aSocketForbids.Select(DataHandler.GetLoot).ToList();
        floorTiles.Clear();
        for (int x = -2; x <= 3; x++) for (int y = -2; y <= 3; y++) {
            var floor = Owner("floor" + x + "-" + y); floor.ship = home; Value(floor, "IsFloor", 1); Value(floor, "IsFloorSealed", 1);
            var tile = Empty<Tile>(); tile.coProps = floor; floorTiles[new Vector2(x,y)] = tile;
        }
        var center = new Vector3(.5f,.5f,0);
        Check(item.CheckFit(center, neighbor), "Native fit rejects residence when reference hull is its docked ship");
        foreach (string template in Eligibility.Templates) {
            home.strTemplateName = template;
            Check(item.CheckFit(center, neighbor), "Native fit rejects residential template " + template);
        }
        home.strTemplateName = null;
        Check(item.CheckFit(center, neighbor), "Native fit rejects existing residence with no template metadata");
        Owners[home.strRegID] = "npc";
        Check(!item.CheckFit(center, neighbor), "Native fit allows unowned apartment"); Owners[home.strRegID] = "player";
        foreach (var tile in floorTiles.Values) tile.coProps.ship = neighbor;
        Check(!item.CheckFit(center, neighbor), "Native fit allows ship alongside owned residence");
        foreach (var tile in floorTiles.Values) tile.coProps.ship = home;
        floorTiles[new Vector2(1,0)].coProps.ship = neighbor;
        Check(!item.CheckFit(center, neighbor), "Native fit allows installation across hull seam");
        floorTiles[new Vector2(1,0)].coProps.ship = home;
        Runtime.Disable(); Check(!item.CheckFit(center, neighbor), "Native fit works without supporting plugin"); Runtime.Enable();
        co.strCODef = "ItmKiosk01";
        foreach (var tile in floorTiles.Values) tile.coProps.ship = neighbor;
        Check(item.CheckFit(center, neighbor), "Private fit adapter changes public kiosk behavior");
        co.strCODef = Runtime.Item + "Loose";
        Check(item.CheckFit(center, neighbor), "Private fit adapter prevents transporting loose item on ships");
        var action = Empty<Interaction>(); action.strName = Runtime.Use;
        Check(!action.Triggered(CrewSim.coPlayer, co), "Actual patched Triggered permits loose terminal use");
        co.strCODef = Runtime.Item; co.ship = neighbor; Value(co, "IsInstalled", 1);
        Check(!action.Triggered(CrewSim.coPlayer, co), "Actual patched Triggered permits service from neighboring ship");
        co.ship = home;
        Runtime.Disable(); Check(!action.Triggered(CrewSim.coPlayer, co), "Actual Triggered bypasses absent plugin gate"); Runtime.Enable();
    }
}
