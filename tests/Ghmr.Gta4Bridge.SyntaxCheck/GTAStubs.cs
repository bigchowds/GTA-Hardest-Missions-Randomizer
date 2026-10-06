using System;

namespace GTA
{
    public class Script
    {
        public int Interval { get; set; }
        public event EventHandler Tick;
        protected Player Player { get; } = new Player();
    }

    public class Player
    {
        public Ped Character { get; } = new Ped();
        public int Money { get; set; }
    }

    public class Ped
    {
        public Vector3 Position { get; set; }
        public bool Exists() { return true; }
    }

    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;
    }

    public enum BlipType
    {
        Vehicle = 1,
        Ped = 2,
        Object = 3,
        Coordinate = 4,
        Contact = 5,
        Pickup = 6,
        Unknown = 7,
        Pickup2 = 8
    }

    public enum BlipIcon
    {
        Person_Packie = 42
    }

    public class Blip
    {
        public BlipIcon Icon { get; set; }
        public Vector3 Position { get; set; }
        public static Blip[] GetAllBlipsOfType(BlipType type)
        {
            return Array.Empty<Blip>();
        }
    }

    public static class Game
    {
        public static void DisplayText(string text, int duration) { }
        public static void FadeScreenOut(int duration, bool wait) { }
        public static void FadeScreenIn(int duration) { }
    }

    public static class World
    {
        public static void LoadEnvironmentNow(Vector3 position) { }
    }
}

namespace GTA.Native
{
    public static class Function
    {
        public static Func<string, object[], object> Handler;
        public static T Call<T>(string name, params object[] arguments)
        {
            if (Handler != null)
            {
                object value = Handler(name, arguments);
                if (value != null) return (T)value;
            }
            return default(T);
        }

        public static void Call(string name, params object[] arguments) { }
    }
}
