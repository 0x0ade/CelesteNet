namespace Celeste.Mod.CelesteNet.DataTypes {
    /*
    While debugging the old UDP protocol, I got so fed up with it that I created
    this overengineered protocol instead. However, as it seems way more stable
    and predictable than the old one, I decided to keep it.

    A UDP connection has two properties which are of interest to us here:
    -> UDP connections can be "initialized"
        This means the connection knows its peer's endpoint, it's receiving and
        sending infrastructure is up and running and is beeing activly monitored
        for heartbeats and keepalives. However, just beeing initialized isn't enough
        for the connection to actually enqueue anything onto it's send queue.
    -> UDP connections can be "established"
        This means the connection has an actual UDP connection ID in addition to
        beeing initialized (if it's not established, it has the ID -1), and data
        packets are actually sent over it.
    If UDP's disabled (!UseUDP), then connections can neither be initialized nor
    established. Also note that UDP scoring mechanisms are outside the scope of
    this protocol.

    An UDP connection is said to have "died" when it switches from initialized
    to unestablished. This protocol only notifies the peer if it happens for an
    established connection, however unestablished connections can die too.

    The client initially starts its UDP connection initialized, but
    unestablished. It remains in this "limbo"-state until the server tells it to
    establish the connection. This is used to prevent sending packets before the
    server's ready to receive them, while still handling timeouts and
    keep-alives like usual.

    This packet is used to coordinate established connections, and has a few
    different meanings, depending on the values in it:
    - ConnectionID < 1
        Disable UDP for this client's connection. This is the only packet
        handled even when no connection is initialized.
    - ConnectionID >= 0 && MaxDatagramSize < 1+MaxPacketSize
        Nofication that the connection died (similarly to how the downgrade
        mechanism determines connection death)
    - ConnectionID >= 0 && MaxDatagramSize >= 1+MaxPacketSize
        New MAXIMUM datagram size. Note that once a connection is initially
        established, it's max datagram size can only go down. If the other peer
        has a different datagram size than here requested BEFORE the change,
        it'll respond an info packet with it's new maximum datagram size.
    
    If a peer (in the current implementation, only the client) has an
    initialized but unestablished connection, when it receives a packet falling
    into the last case, and it doesn't refer to an old connection
    (ConnectionID <= udpLastConnectionID), it'll establish it's connection with
    the parameters specified in the packet. This mechanism is used by the server
    to notify the client when it established the UDP connection, and it's ready
    to receive data.

    Example packet exchange
                    CLIENT                                                        SERVER
            <connect via UDP>
    <initialize unestablished connection>
                                            -> (connection token)
                                                                            <establish connection>
                                            <- UDPInfo(0, 4096)
            <establish connection>


            <downgrade connection>
                                            -> UDPInfo(0, 2048)
                                            <- UDPInfo(0, 2048)

                                                                            <connection death>
                                            <- UDPInfo(0, 0)
            <connection death>
        <client decides to disable UDP>
                                            -> UDPInfo(-1, 0)
                                                                            <won't accept UDP connections from client>
    */
    public class DataLowLevelUDPInfo : DataType<DataLowLevelUDPInfo> {

        static DataLowLevelUDPInfo() {
            DataID = "udpInfo";
        }

        public override DataFlags DataFlags => DataFlags.SlimHeader | DataFlags.Small;

        public int ConnectionID;
        public int MaxDatagramSize;

        protected override void Read(CelesteNetBinaryReader reader) {
            ConnectionID = reader.Read7BitEncodedInt();
            MaxDatagramSize = reader.Read7BitEncodedInt();
        }

        protected override void Write(CelesteNetBinaryWriter writer) {
            writer.Write7BitEncodedInt(ConnectionID);
            writer.Write7BitEncodedInt(MaxDatagramSize);
        }

    }
}