namespace Celeste.Mod.CelesteNet.DataTypes {
    public class DataReady : DataType<DataReady> {

        static DataReady() {
            DataID = "ready";
        }

        public override DataFlags DataFlags => DataFlags.Small;

    }
}