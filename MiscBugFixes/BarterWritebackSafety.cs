using System.Collections.Generic;
using Ostranauts.Ships;
using Ostranauts.ShipGUIs.Trade;

namespace Ostrabus.MiscBugFixes;

internal static class BarterWritebackSafety {
    internal static void Append(Ship ship, List<JsonItem> added, CondOwner co) {
        var position = co.tf.position;
        try {
            co.tf.position = position - AsyncShipLoader.SPAWNOFFSET;
            string marker = GUITradeBase.ASYNCIDENTIFIER;
            if (!co.mapGUIPropMaps.ContainsKey(marker))
                co.mapGUIPropMaps.Add(marker, new Dictionary<string, string> { [marker] = marker });
            added.Add(ship.GetJsonItem(co));
            var saved = co.GetJSONSave();
            if (saved != null) DataHandler.dictCOSaves[saved.strID] = saved;
        } finally { co.tf.position = position; }
    }
}
