using System;

namespace GTA
{
    public class Script
    {
        public int Interval { get; set; }

        public event EventHandler Tick
        {
            add { }
            remove { }
        }

        public event EventHandler Aborted
        {
            add { }
            remove { }
        }
    }

    public static class Game
    {
        public static bool IsMissionActive => false;
        public static bool IsPaused => false;
        public static bool IsCutsceneActive => false;
        public static Player Player { get; } = new();
        public static object Version => "stub";
        public static int GenerateHash(string value) => value.GetHashCode();
    }

    public sealed class Player
    {
        public Ped Character { get; } = new();
    }

    public sealed class Ped
    {
        public bool IsDead => false;
        public bool Exists() => true;
    }

    public sealed class GlobalVariable
    {
        public static GlobalVariable Get(int index)
        {
            _ = index;
            return new GlobalVariable();
        }

        public T Read<T>() => default!;

        public void Write<T>(T value)
        {
            _ = value;
        }

        public bool IsBitSet(int bit)
        {
            _ = bit;
            return false;
        }

        public void SetBit(int bit)
        {
            _ = bit;
        }

        public void ClearBit(int bit)
        {
            _ = bit;
        }
    }
}

namespace GTA.Native
{
    public enum Hash
    {
        GET_NUMBER_OF_THREADS_RUNNING_THE_SCRIPT_WITH_THIS_HASH,
        GET_TIME_SINCE_LAST_DEATH,
        IS_PLAYER_CONTROL_ON,
        NETWORK_IS_GAME_IN_PROGRESS,
        IS_WARNING_MESSAGE_ACTIVE,
        SET_CONTROL_VALUE_NEXT_FRAME
    }

    public static class Function
    {
        public static T Call<T>(Hash hash, params object[] arguments)
        {
            _ = hash;
            _ = arguments;
            return default!;
        }
    }
}

namespace GTA.UI
{
    public static class Screen
    {
        public static void ShowSubtitle(string message, int duration)
        {
            _ = message;
            _ = duration;
        }
    }
}
