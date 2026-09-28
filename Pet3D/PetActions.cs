namespace ChickenDesktopPet3D;

internal static class PetActions
{
    public static bool Loops(string action) => action is "idle" or "idle2" or "squat" or "walk" or "sleep";
    public static bool HasSeparateFraming(string action) =>
        !Loops(action) && action is not ("react" or "react2");

    public static string Label(string action) => action switch
    {
        "idle" or "idle2" => "自在休息",
        "feed" => "喂食",
        "sleep" => "睡觉",
        "wake" => "叫醒",
        "walk" => "散步",
        "react" or "react2" => "回应",
        "trick" or "trick2" => "表演",
        _ => PetUiResources.DefaultActivities.FirstOrDefault(activity => activity.Id == action)?.Label ?? action,
    };
}
