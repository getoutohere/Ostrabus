namespace Ostrabus.QoL.Socials;

internal static class ContactVisibility {
    static bool ConstructionType(string type) => type == "Robot01" || type == "Robot02";
    // Social PersonSpec serialization omits strType. The resolved CO's saved
    // definition/type still identifies drones after reloading an existing save.
    internal static bool IsConstructionDrone(PersonSpec person, CondOwner character) =>
        person != null && ConstructionType(person.strType) || !ReferenceEquals(character, null) &&
        (ConstructionType(character.strCODef) || ConstructionType(character.strType));
    internal static bool Hide(Relationship contact) {
        var person = contact?.pspec;
        if (person == null) return true;
        var character = person.GetCO();
        return character == null || IsConstructionDrone(person, character);
    }
}
