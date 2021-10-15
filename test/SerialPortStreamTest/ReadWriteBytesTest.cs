﻿// Copyright © Jason Curl 2012-2021
// Sources at https://github.com/jcurl/SerialPortStream
// Licensed under the Microsoft Public License (Ms-PL)

namespace RJCP.IO.Ports
{
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using NUnit.Framework;
    using Serial;

    [TestFixture]
    [Timeout(20000)]
    public class ReadWriteBytesTest
    {
        [Test]
        public void ReadLargeDataBlock()
        {
            Random rnd = new Random();
            byte[] receiveData = new byte[262144];
            rnd.NextBytes(receiveData);

            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";

                // The task simulates receiving data
                Task driverTask = new TaskFactory().StartNew(() => {
                    Random l = new Random();

                    while (!serial.IsRunning) {
                        Thread.Sleep(1);
                    }

                    int p = 0;
                    while (p < receiveData.Length) {
                        int q = Math.Min(l.Next(1, 768), receiveData.Length - p);
                        int w = serial.VirtualBuffer.WriteReceivedData(receiveData, p, q);
                        Console.WriteLine($"VirtualBuffer.WriteReceivedData(receiveData, {p}, {q}) = {w}");
                        p += w;
                        Thread.Sleep(1);
                    }
                });

                // Receive the data, as if it had arrived from a serial port
                byte[] receiveBuffer = new byte[receiveData.Length];
                stream.Open();
                int r = 0;
                while (r < receiveBuffer.Length) {
                    r += stream.Read(receiveBuffer, r, Math.Min(receiveBuffer.Length - r, 16));
                }

                driverTask.Wait();
                Assert.That(receiveBuffer, Is.EqualTo(receiveData));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        public void WriteLargeDataBlock(int writeDelay)
        {
            Random rnd = new Random();
            byte[] sendData = new byte[262144];
            rnd.NextBytes(sendData);

            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";

                // The task simulates receiving data
                byte[] sentData = new byte[sendData.Length];
                Task driverTask = new TaskFactory().StartNew(() => {
                    while (!serial.IsRunning) {
                        Thread.Sleep(1);
                    }

                    int p = 0;
                    while (p < sentData.Length) {
                        int r = serial.VirtualBuffer.ReadSentData(sentData, p, sentData.Length - p);
                        Console.WriteLine($"VirtualBuffer.ReadSentData(sentData, {p}, {sentData.Length - p}) = {r}");
                        p += r;
                        Thread.Sleep(1);
                    }
                });

                // Receive the data, as if it had arrived from a serial port
                stream.Open();
                int s = 0;
                Random l = new Random();

                while (s < sendData.Length) {
                    int q = Math.Min(l.Next(1, 2048), sendData.Length - s);
                    stream.Write(sendData, s, q);
                    Console.WriteLine($"Stream.Write(sendData, {s}, {q})");
                    s += q;
                    if (writeDelay > 0) Thread.Sleep(writeDelay);
                }

                driverTask.Wait();
                Assert.That(sentData, Is.EqualTo(sendData));
            }
        }

        [Test]
        public void WriteFullBuffer()
        {
            Random rnd = new Random();
            byte[] sendData = new byte[1024];
            byte[] sentData = new byte[1024];
            rnd.NextBytes(sendData);

            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.WriteBufferSize = 1024;
                stream.ReadBufferSize = 1024;
                stream.PortName = "COM";

                stream.Open();
                stream.Write(sendData, 0, 1024);

                Task serialPort = new TaskFactory().StartNew(() => {
                    // This will block, because the buffer is full
                    stream.Write(sendData, 0, 1024);
                });

                Assert.That(serialPort.Wait(250), Is.False);

                serial.VirtualBuffer.ReadSentData(sentData, 0, 1024);
                serialPort.Wait();

                Assert.That(serial.VirtualBuffer.SentDataLength, Is.EqualTo(1024));
            }
        }

        [Test]
        public void DataReceivedEvent()
        {
            Random rnd = new Random();
            byte[] serialBuffer = new byte[256];
            rnd.NextBytes(serialBuffer);

            byte[] readBuffer = new byte[256];

            using (ManualResetEventSlim mre = new ManualResetEventSlim(false))
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.Open();

                // The event is run on a thread-pool thread, so we need to wait for it to be triggered.
                int r = -1;
                stream.DataReceived += (s, e) => {
                    if (e.EventType == SerialData.Chars) {
                        r = stream.Read(readBuffer, 0, 256);
                        mre.Set();
                    }
                };

                int w = serial.VirtualBuffer.WriteReceivedData(serialBuffer, 0, serialBuffer.Length);
                Assert.That(w, Is.EqualTo(serialBuffer.Length));

                mre.Wait();
                Assert.That(r, Is.EqualTo(readBuffer.Length));
                Assert.That(readBuffer, Is.EqualTo(serialBuffer));
            }
        }

        [Test]
        public void DataReceivedEventThreshold()
        {
            Random rnd = new Random();
            byte[] serialBuffer = new byte[256];
            rnd.NextBytes(serialBuffer);

            byte[] readBuffer = new byte[256];

            using (ManualResetEventSlim mre = new ManualResetEventSlim(false))
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.ReceivedBytesThreshold = 100;
                stream.Open();

                // The event is run on a thread-pool thread, so we need to wait for it to be triggered.
                int r = -1;
                stream.DataReceived += (s, e) => {
                    if (e.EventType == SerialData.Chars) {
                        r = stream.Read(readBuffer, 0, 256);
                        mre.Set();
                    }
                };

                int w = serial.VirtualBuffer.WriteReceivedData(serialBuffer, 0, 50);
                Assert.That(w, Is.EqualTo(50));
                Assert.That(mre.Wait(100), Is.False); // Need 100 bytes, have 50 bytes, so should timeout

                w = serial.VirtualBuffer.WriteReceivedData(serialBuffer, 50, 49);
                Assert.That(w, Is.EqualTo(49));
                Assert.That(mre.Wait(100), Is.False); // Need 100 bytes, have 99 bytes, so should timeout

                w = serial.VirtualBuffer.WriteReceivedData(serialBuffer, 99, 1);
                Assert.That(w, Is.EqualTo(1));
                Assert.That(mre.Wait(100), Is.True); // Need 100 bytes, have 99 bytes, so should timeout

                Assert.That(r, Is.EqualTo(100));
                Assert.That(readBuffer.Take(100), Is.EqualTo(serialBuffer.Take(100)));
            }
        }

        [Test]
        public void ReadByte()
        {
            Random rnd = new Random();
            byte[] receiveData = new byte[256];
            rnd.NextBytes(receiveData);

            using (ManualResetEventSlim mre = new ManualResetEventSlim(false))
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.Open();

                Task driverTask = new TaskFactory().StartNew(() => {
                    Random l = new Random();

                    while (!serial.IsRunning) {
                        Thread.Sleep(1);
                    }

                    int p = 0;
                    while (p < receiveData.Length) {
                        int q = Math.Min(l.Next(1, 768), receiveData.Length - p);
                        int w = serial.VirtualBuffer.WriteReceivedData(receiveData, p, q);
                        p += w;
                        Thread.Sleep(1);
                    }
                });

                // Receive the data, as if it had arrived from a serial port
                byte[] receiveBuffer = new byte[receiveData.Length];
                int r = 0;
                while (r < receiveBuffer.Length) {
                    int value = stream.ReadByte();
                    Assert.That(value, Is.GreaterThanOrEqualTo(0));
                    receiveBuffer[r] = (byte)value;
                    r++;
                }

                driverTask.Wait();
                Assert.That(receiveBuffer, Is.EqualTo(receiveData));
            }
        }

        [Test]
        public void ReadNotOpen()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";

                Assert.That(stream.IsOpen, Is.False);
                Assert.That(stream.CanRead, Is.True);
                int r = stream.Read(new byte[128], 0, 128);
                Assert.That(r, Is.EqualTo(0));
            }
        }

        [Test]
        public void WriteNotOpen()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";

                Assert.That(stream.IsOpen, Is.False);
                Assert.That(stream.CanWrite, Is.False);

                Assert.That(() => {
                    stream.Write(new byte[128], 0, 128);
                }, Throws.TypeOf<InvalidOperationException>());
            }
        }

        [Test]
        public void DiscardOutBuffer()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.Open();

                stream.Write(new byte[1024], 0, 1024);
                Assert.That(serial.VirtualBuffer.SentDataLength, Is.EqualTo(1024));

                stream.DiscardOutBuffer();
                Assert.That(serial.VirtualBuffer.SentDataLength, Is.EqualTo(0));
            }
        }

        [Test]
        public void DiscardInBuffer()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.Open();

                int w = serial.VirtualBuffer.WriteReceivedData(new byte[1024], 0, 1024);
                Assert.That(w, Is.EqualTo(1024));
                Assert.That(serial.VirtualBuffer.ReceivedDataLength, Is.EqualTo(1024));

                stream.DiscardInBuffer();
                Assert.That(serial.VirtualBuffer.ReceivedDataLength, Is.EqualTo(0));
            }
        }

        [Test]
        public void CanTimeout()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                Assert.That(stream.CanTimeout, Is.True);
                Assert.That(stream.ReadTimeout, Is.EqualTo(Timeout.Infinite));
                Assert.That(stream.WriteTimeout, Is.EqualTo(Timeout.Infinite));
            }
        }

        [Test]
        public void ReadTimeout()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.ReadTimeout = 100;
                stream.Open();

                Assert.That(stream.Read(new byte[128], 0, 128), Is.EqualTo(0));
            }
        }

        [Test]
        public void ReadByteTimeout()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.ReadTimeout = 100;
                stream.Open();

                Assert.That(stream.ReadByte(), Is.EqualTo(-1));
            }
        }

        [Test]
        public void WriteTimeout()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.WriteTimeout = 100;
                stream.WriteBufferSize = 2048;
                stream.Open();

                stream.Write(new byte[stream.WriteBufferSize], 0, stream.WriteBufferSize);
                Assert.That(() => {
                    stream.Write(new byte[stream.WriteBufferSize], 0, stream.WriteBufferSize);
                }, Throws.TypeOf<TimeoutException>());
            }
        }

        [Test]
        public void Flush()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.WriteBufferSize = 2048;
                stream.Open();

                stream.Write(new byte[stream.WriteBufferSize], 0, stream.WriteBufferSize);

                Task driverTask = new TaskFactory().StartNew(() => {
                    byte[] buffer = new byte[128];
                    Thread.Sleep(100);

                    while (!serial.IsRunning) {
                        Thread.Sleep(1);
                    }

                    while (serial.VirtualBuffer.SentDataLength > 0) {
                        serial.VirtualBuffer.ReadSentData(buffer, 0, buffer.Length);
                        Thread.Sleep(1);
                    }
                });
                stream.Flush();

                driverTask.Wait();
            }
        }

        [Test]
        public void FlushTimeout()
        {
            using (VirtualNativeSerial serial = new VirtualNativeSerial())
            using (SerialPortStream stream = new SerialPortStream(serial)) {
                stream.PortName = "COM";
                stream.WriteBufferSize = 2048;
                stream.WriteTimeout = 100;
                stream.Open();

                stream.Write(new byte[stream.WriteBufferSize], 0, stream.WriteBufferSize);

                // There's no thread to read serial.VirtualBuffer.ReadSentData, so it should time out.
                Assert.That(() => {
                    stream.Flush();
                }, Throws.TypeOf<TimeoutException>());
            }
        }
    }
}