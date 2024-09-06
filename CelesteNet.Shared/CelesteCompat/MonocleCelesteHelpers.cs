namespace Celeste.Mod.CelesteNet.MonocleCelesteHelpers {

    // some enums copied straight over from vanilla Celeste,
    // for use here in Shared assembly when building Server with no Celeste

    public enum AreaMode {
        Normal,
        BSide,
        CSide
    }

    public enum Facings {
        Right = 1,
        Left = -1
    }

    public enum PlayerSpriteMode {
        Madeline,
        MadelineNoBackpack,
        Badeline,
        MadelineAsBadeline,
        Playback
    }

}
