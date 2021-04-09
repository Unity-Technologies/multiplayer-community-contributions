using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using MLAPI.Cryptography.KeyExchanges;
using MLAPI.Logging;
using MLAPI.Serialization;
using MLAPI.Serialization.Pooled;
using MLAPI.Transport.ChaCha20.ChaCha20;
using MLAPI.Transports.Tasks;
using UnityEngine;

namespace MLAPI.Transport.ChaCha20
{
    public class CryptographyTransportAdapter : NetworkTransport
    {
        public override ulong ServerClientId => Transport.ServerClientId;

        public NetworkTransport Transport;

        public bool SignKeyExchange;

        [TextArea]
        public string ServerBase64PFX;

        private X509Certificate2 m_ServerCertificate;

        private byte[] m_ServerCertificateBytes
        {
            get
            {
                if (m_ServerCertificatesByteBacking == null)
                {
                    m_ServerCertificatesByteBacking = m_ServerCertificate.Export(X509ContentType.Cert);
                }

                return m_ServerCertificatesByteBacking;
            }
        }

        private byte[] m_ServerCertificatesByteBacking;

        // State
        private bool m_IsServer;

        // Used by client
        private ECDiffieHellmanRSA m_ServerSignedKeyExchange;
        private ECDiffieHellman m_ServerKeyExchange;

        // Used by server
        private readonly Dictionary<ulong, ECDiffieHellmanRSA> m_ClientSignedKeyExchanges = new Dictionary<ulong, ECDiffieHellmanRSA>();
        private readonly Dictionary<ulong, ECDiffieHellman> m_ClientKeyExchanges = new Dictionary<ulong, ECDiffieHellman>();

        public byte[] ServerKey { get; private set; }
        public readonly Dictionary<ulong, byte[]> ClientKeys = new Dictionary<ulong, byte[]>();

        private readonly Dictionary<ulong, ChaCha20Cipher> m_ClientCiphers = new Dictionary<ulong, ChaCha20Cipher>();
        private ChaCha20Cipher m_ServerCipher;

        private readonly Dictionary<ulong, ClientState> m_ClientStates = new Dictionary<ulong, ClientState>();

        private enum ClientState : byte
        {
            WaitingForHailResponse,
            Connected
        }

        // Max message size
        private byte[] m_CryptoBuffer = new byte[1024 * 8];

        private enum MessageType : byte
        {
            Hail, // Server->Client
            HailResponse, // Client->Server
            Ready, // Server->Client
            Internal // MLAPI Message
        }

        public override void Send(ulong clientId, ArraySegment<byte> data, NetworkChannel networkChannel)
        {
            using (PooledNetworkBuffer buffer = PooledNetworkBuffer.Get())
            using (PooledNetworkWriter writer = PooledNetworkWriter.Get(buffer))
            {
                // Write message type
                writer.WriteBits((byte)MessageType.Internal, 2);

                // Align bits
                writer.WritePadBits();

                // Get the ChaCha20 cipher
                ChaCha20Cipher cipher = clientId == ServerClientId ? m_ServerCipher : m_ClientCiphers[clientId];

                // Store position (length messes with it)
                long position = buffer.Position;

                // Expand buffer with data count
                buffer.SetLength(buffer.Length + data.Count);

                // Restore position
                buffer.Position = position;

                // Encrypt with ChaCha
                cipher.ProcessBytes(data.Array, data.Offset, buffer.GetBuffer(), (int)buffer.Position, data.Count);


                // Send the encrypted format
                Transport.Send(clientId, new ArraySegment<byte>(buffer.GetBuffer(), 0, (int)buffer.Length), networkChannel);
            }
        }

        private readonly NetworkBuffer m_DataBuffer = new NetworkBuffer();
        public override NetworkEvent PollEvent(out ulong clientId, out NetworkChannel networkChannel, out ArraySegment<byte> payload, out float receiveTime)
        {
            NetworkEvent @event = Transport.PollEvent(out ulong internalClientId, out NetworkChannel internalNetworkChannel, out ArraySegment<byte> internalPayload, out float internalReceiveTime);

            if (@event == NetworkEvent.Connect && m_IsServer && !m_ClientStates.ContainsKey(internalClientId))
            {
                // Send server a handshake
                using (PooledNetworkBuffer hailBuffer = PooledNetworkBuffer.Get())
                using (PooledNetworkWriter hailWriter = PooledNetworkWriter.Get(hailBuffer))
                {
                    // Write message type
                    hailWriter.WriteBits((byte)MessageType.Hail, 2);

                    // Write if key exchange should be signed
                    hailWriter.WriteBit(SignKeyExchange);

                    // Pad bits
                    hailWriter.WritePadBits();

                    if (SignKeyExchange)
                    {
                        // Create handshake parameters
                        ECDiffieHellmanRSA keyExchange = new ECDiffieHellmanRSA(m_ServerCertificate);
                        m_ClientSignedKeyExchanges.Add(internalClientId, keyExchange);

                        // Write public part of RSA key
                        hailWriter.WriteByteArray(m_ServerCertificateBytes);

                        // Write key exchange public part
                        hailWriter.WriteByteArray(keyExchange.GetSecurePublicPart());
                    }
                    else
                    {
                        // Create handshake parameters
                        ECDiffieHellman keyExchange = new ECDiffieHellman();
                        m_ClientKeyExchanges.Add(internalClientId, keyExchange);

                        // Write key exchange public part
                        hailWriter.WriteByteArray(keyExchange.GetPublicKey());
                    }

                    // Send hail
                    Transport.Send(internalClientId, new ArraySegment<byte>(hailBuffer.GetBuffer(), 0, (int)hailBuffer.Length), NetworkChannel.Internal);
                }

                // Add them to client state
                m_ClientStates.Add(internalClientId, ClientState.WaitingForHailResponse);

                clientId = internalClientId;
                networkChannel = NetworkChannel.Internal;
                payload = new ArraySegment<byte>();
                receiveTime = internalReceiveTime;
                return NetworkEvent.Nothing;
            }
            else if (@event == NetworkEvent.Data)
            {
                // Set the data to the buffer
                m_DataBuffer.SetTarget(internalPayload.Array);
                m_DataBuffer.SetLength(internalPayload.Count + internalPayload.Offset);
                m_DataBuffer.Position = internalPayload.Offset;

                using (PooledNetworkReader dataReader = PooledNetworkReader.Get(m_DataBuffer))
                {
                    MessageType messageType = (MessageType)dataReader.ReadByteBits(2);

                    if (messageType == MessageType.Hail && !m_IsServer)
                    {
                        // Server sent us a hail

                        // Read if the data was signed
                        bool sign = dataReader.ReadBit();

                        if (sign != SignKeyExchange)
                        {
                            if (NetworkLog.CurrentLogLevel <= LogLevel.Error)
                            {
                                NetworkLog.LogError("Mismatch between " + nameof(SignKeyExchange));
                            }

                            clientId = internalClientId;
                            networkChannel = NetworkChannel.Internal;
                            payload = new ArraySegment<byte>();
                            receiveTime = internalReceiveTime;
                            return NetworkEvent.Nothing;
                        }

                        // Align bits
                        dataReader.SkipPadBits();

                        if (SignKeyExchange)
                        {
                            // Read certificate
                            m_ServerCertificate = new X509Certificate2(dataReader.ReadByteArray());

                            // TODO: IMPORTANT!!! VERIFY CERTIFICATE!!!!!!!

                            // Create key exchange
                            m_ServerSignedKeyExchange = new ECDiffieHellmanRSA(m_ServerCertificate);

                            // Read servers public part
                            byte[] serverPublicPart = dataReader.ReadByteArray();

                            // Get shared
                            byte[] key = m_ServerSignedKeyExchange.GetVerifiedSharedPart(serverPublicPart);

                            // Do key stretching with PBKDF2-HMAC-SHA1 with Application.productName as salt
                            using (Rfc2898DeriveBytes pbdkf = new Rfc2898DeriveBytes(key, Encoding.UTF8.GetBytes(Application.productName), 10_000))
                            {
                                // Add raw key for external use
                                ServerKey = key;

                                // ChaCha wants 48 bytes
                                byte[] chaChaData = pbdkf.GetBytes(48);

                                // Get key part
                                byte[] chaChaKey = new byte[32];
                                Buffer.BlockCopy(chaChaData, 0, chaChaKey, 0, 32);

                                // Get nonce part
                                byte[] chaChaNonce = new byte[12];
                                Buffer.BlockCopy(chaChaData, 32, chaChaNonce, 0, 12);

                                // Create cipher
                                m_ServerCipher = new ChaCha20Cipher(chaChaKey, chaChaNonce, BitConverter.ToUInt32(chaChaData, 32 + 12));
                            }
                        }
                        else
                        {
                            // Create key exchange
                            m_ServerKeyExchange = new ECDiffieHellman();

                            // Read servers public part
                            byte[] serverPublicPart = dataReader.ReadByteArray();

                            // Get shared
                            byte[] key = m_ServerKeyExchange.GetSharedSecretRaw(serverPublicPart);

                            // Do key stretching with PBKDF2-HMAC-SHA1 with Application.productName as salt
                            using (Rfc2898DeriveBytes pbdkf = new Rfc2898DeriveBytes(key, Encoding.UTF8.GetBytes(Application.productName), 10_000))
                            {
                                // Add raw key for external use
                                ServerKey = key;

                                // ChaCha wants 48 bytes
                                byte[] chaChaData = pbdkf.GetBytes(48);

                                // Get key part
                                byte[] chaChaKey = new byte[32];
                                Buffer.BlockCopy(chaChaData, 0, chaChaKey, 0, 32);

                                // Get nonce part
                                byte[] chaChaNonce = new byte[12];
                                Buffer.BlockCopy(chaChaData, 32, chaChaNonce, 0, 12);

                                // Create cipher
                                m_ServerCipher = new ChaCha20Cipher(chaChaKey, chaChaNonce, BitConverter.ToUInt32(chaChaData, 32 + 12));
                            }
                        }

                        // Respond with hail response
                        using (PooledNetworkBuffer hailResponseBuffer = PooledNetworkBuffer.Get())
                        using (PooledNetworkWriter hailResponseWriter = PooledNetworkWriter.Get(hailResponseBuffer))
                        {
                            // Write message type
                            hailResponseWriter.WriteBits((byte)MessageType.HailResponse, 2);

                            // Align bits
                            hailResponseWriter.WritePadBits();

                            if (SignKeyExchange)
                            {
                                // Write public part
                                hailResponseWriter.WriteByteArray(m_ServerSignedKeyExchange.GetSecurePublicPart());
                            }
                            else
                            {
                                // Write public part
                                hailResponseWriter.WriteByteArray(m_ServerKeyExchange.GetPublicKey());
                            }

                            // Send hail response
                            Transport.Send(internalClientId, new ArraySegment<byte>(hailResponseBuffer.GetBuffer(), 0, (int)hailResponseBuffer.Length), NetworkChannel.Internal);
                        }

                        clientId = internalClientId;
                        networkChannel = NetworkChannel.Internal;
                        payload = new ArraySegment<byte>();
                        receiveTime = internalReceiveTime;
                        return NetworkEvent.Nothing;
                    }
                    else if (messageType == MessageType.HailResponse && m_IsServer && m_ClientStates.ContainsKey(internalClientId) && m_ClientStates[internalClientId] == ClientState.WaitingForHailResponse)
                    {
                        // Client sent us a hail response

                        // Align bits
                        dataReader.SkipPadBits();

                        // Read clients public part
                        byte[] clientPublicPart = dataReader.ReadByteArray();

                        if (SignKeyExchange)
                        {
                            // Get key
                            byte[] key = m_ClientSignedKeyExchanges[internalClientId].GetVerifiedSharedPart(clientPublicPart);

                            // Do key stretching with PBKDF2-HMAC-SHA1 with Application.productName as salt
                            using (Rfc2898DeriveBytes pbdkf = new Rfc2898DeriveBytes(key, Encoding.UTF8.GetBytes(Application.productName), 10_000))
                            {
                                // Add raw key for external use
                                ClientKeys.Add(internalClientId, key);

                                // ChaCha wants 48 bytes
                                byte[] chaChaData = pbdkf.GetBytes(48);

                                // Get key part
                                byte[] chaChaKey = new byte[32];
                                Buffer.BlockCopy(chaChaData, 0, chaChaKey, 0, 32);

                                // Get nonce part
                                byte[] chaChaNonce = new byte[12];
                                Buffer.BlockCopy(chaChaData, 32, chaChaNonce, 0, 12);

                                // Create cipher
                                m_ClientCiphers.Add(internalClientId, new ChaCha20Cipher(chaChaKey, chaChaNonce, BitConverter.ToUInt32(chaChaData, 32 + 12)));
                            }

                            // Cleanup
                            m_ClientSignedKeyExchanges.Remove(internalClientId);
                        }
                        else
                        {
                            // Get key
                            byte[] key = m_ClientKeyExchanges[internalClientId].GetSharedSecretRaw(clientPublicPart);

                            // Do key stretching with PBKDF2-HMAC-SHA1 with Application.productName as salt
                            using (Rfc2898DeriveBytes pbdkf = new Rfc2898DeriveBytes(key, Encoding.UTF8.GetBytes(Application.productName), 10_000))
                            {
                                // Add raw key for external use
                                ClientKeys.Add(internalClientId, key);

                                // ChaCha wants 48 bytes
                                byte[] chaChaData = pbdkf.GetBytes(48);

                                // Get key part
                                byte[] chaChaKey = new byte[32];
                                Buffer.BlockCopy(chaChaData, 0, chaChaKey, 0, 32);

                                // Get nonce part
                                byte[] chaChaNonce = new byte[12];
                                Buffer.BlockCopy(chaChaData, 32, chaChaNonce, 0, 12);

                                // Create cipher
                                m_ClientCiphers.Add(internalClientId, new ChaCha20Cipher(chaChaKey, chaChaNonce, BitConverter.ToUInt32(chaChaData, 32 + 12)));
                            }

                            //Cleanup
                            m_ClientKeyExchanges.Remove(internalClientId);
                        }

                        // Respond with ready response
                        using (PooledNetworkBuffer readyResponseBuffer = PooledNetworkBuffer.Get())
                        using (PooledNetworkWriter readyResponseWriter = PooledNetworkWriter.Get(readyResponseBuffer))
                        {
                            // Write message type
                            readyResponseWriter.WriteBits((byte)MessageType.Ready, 2);

                            // Align bits
                            readyResponseWriter.WritePadBits();

                            // Send ready message
                            Transport.Send(internalClientId, new ArraySegment<byte>(readyResponseBuffer.GetBuffer(), 0, (int)readyResponseBuffer.Length), NetworkChannel.Internal);
                        }

                        // Elevate to connected
                        m_ClientStates[internalClientId] = ClientState.Connected;

                        clientId = internalClientId;
                        networkChannel = internalNetworkChannel;
                        payload = new ArraySegment<byte>();
                        receiveTime = internalReceiveTime;
                        return NetworkEvent.Connect;
                    }
                    else if (messageType == MessageType.Ready && !m_IsServer)
                    {
                        // Server is ready for us!
                        // Let the MLAPI know we are connected
                        clientId = internalClientId;
                        networkChannel = internalNetworkChannel;
                        payload = new ArraySegment<byte>();
                        receiveTime = internalReceiveTime;
                        return NetworkEvent.Connect;
                    }
                    else if (messageType == MessageType.Internal && (!m_IsServer || (m_ClientStates.ContainsKey(internalClientId) && m_ClientStates[internalClientId] == ClientState.Connected)))
                    {
                        // Decrypt and pass message to the MLAPI

                        // Align bits
                        dataReader.SkipPadBits();

                        // Get the correct cipher
                        ChaCha20Cipher cipher = m_IsServer ? m_ClientCiphers[internalClientId] : m_ServerCipher;

                        // Decrypt bytes
                        cipher.ProcessBytes(m_DataBuffer.GetBuffer(), (int)m_DataBuffer.Position, m_CryptoBuffer, 0, (int)(m_DataBuffer.Length - m_DataBuffer.Position));

                        clientId = internalClientId;
                        networkChannel = internalNetworkChannel;
                        payload = new ArraySegment<byte>(m_CryptoBuffer, 0, (int)(m_DataBuffer.Length - m_DataBuffer.Position));
                        receiveTime = internalReceiveTime;
                        return NetworkEvent.Data;
                    }
                }
            }
            else if (@event == NetworkEvent.Disconnect)
            {
                // Cleanup

                if (m_IsServer)
                {
                    if (SignKeyExchange)
                    {
                        if (m_ClientSignedKeyExchanges.ContainsKey(internalClientId))
                        {
                            m_ClientSignedKeyExchanges.Remove(internalClientId);
                        }
                    }
                    else
                    {
                        if (m_ClientKeyExchanges.ContainsKey(internalClientId))
                        {
                            m_ClientKeyExchanges.Remove(internalClientId);
                        }
                    }

                    if (ClientKeys.ContainsKey(internalClientId))
                    {
                        ClientKeys.Remove(internalClientId);
                    }

                    if (m_ClientCiphers.ContainsKey(internalClientId))
                    {
                        m_ClientCiphers[internalClientId].Dispose();
                        m_ClientCiphers.Remove(internalClientId);
                    }

                    if (m_ClientStates.ContainsKey(internalClientId))
                    {
                        m_ClientStates.Remove(internalClientId);
                    }
                }
                else
                {
                    m_ServerSignedKeyExchange = null;
                    m_ServerKeyExchange = null;
                    ServerKey = null;
                }

                clientId = internalClientId;
                networkChannel = internalNetworkChannel;
                payload = new ArraySegment<byte>();
                receiveTime = internalReceiveTime;
                return NetworkEvent.Disconnect;
            }

            clientId = internalClientId;
            networkChannel = internalNetworkChannel;
            payload = new ArraySegment<byte>();
            receiveTime = 0;
            return NetworkEvent.Nothing;
        }

        public override SocketTasks StartClient()
        {
            m_IsServer = false;
            return Transport.StartClient();
        }

        public override SocketTasks StartServer()
        {
            m_IsServer = true;
            ParsePFX();
            return Transport.StartServer();
        }

        public override void DisconnectRemoteClient(ulong clientId)
        {
            Transport.DisconnectRemoteClient(clientId);
        }

        public override void DisconnectLocalClient()
        {
            Transport.DisconnectLocalClient();
        }

        public override ulong GetCurrentRtt(ulong clientId)
        {
            return Transport.GetCurrentRtt(clientId);
        }

        public override void Shutdown()
        {
            Transport.Shutdown();
        }

        public override void Init()
        {
            Transport.Init();
        }

        private void ParsePFX()
        {
            try
            {
                string pfx = ServerBase64PFX.Trim();

                try
                {
                    if (m_IsServer && SignKeyExchange && !string.IsNullOrWhiteSpace(pfx))
                    {
                        byte[] decodedPfx = Convert.FromBase64String(ServerBase64PFX);

                        m_ServerCertificate = new X509Certificate2(decodedPfx);

                        if (!m_ServerCertificate.HasPrivateKey)
                        {
                            if (NetworkLog.CurrentLogLevel <= LogLevel.Normal)
                            {
                                NetworkLog.LogWarning("The imported PFX file did not have a private key");
                            }
                        }
                    }
                }
                catch (FormatException e)
                {
                    if (NetworkLog.CurrentLogLevel <= LogLevel.Error)
                    {
                        NetworkLog.LogError("Parsing PFX failed: " + e);
                    }
                }
            }
            catch (CryptographicException e)
            {
                if (NetworkLog.CurrentLogLevel <= LogLevel.Error)
                {
                    NetworkLog.LogError("Importing of certificate failed: " + e);
                }
            }
        }
    }
}
