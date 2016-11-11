﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
// ReSharper disable UseNullPropagation

namespace EdiabasLib
{
    public class EdBluetoothInterface : EdBluetoothInterfaceBase
    {
        public const string PortId = "BLUETOOTH";
        private static readonly long TickResolMs = Stopwatch.Frequency/1000;
        private const int ReadTimeoutOffset = 1000;
        protected const int EchoTimeout = 500;
        protected static System.IO.Ports.SerialPort SerialPort;
        protected static AutoResetEvent CommReceiveEvent;
        protected static Stopwatch StopWatch = new Stopwatch();

        static EdBluetoothInterface()
        {
            SerialPort = new System.IO.Ports.SerialPort();
            SerialPort.DataReceived += SerialDataReceived;
            CommReceiveEvent = new AutoResetEvent(false);
        }

        public static EdiabasNet Ediabas { get; set; }

        public static bool InterfaceConnect(string port, object parameter)
        {
            if (SerialPort.IsOpen)
            {
                return true;
            }
            FastInit = false;
            AdapterType = -1;
            AdapterVersion = -1;

            if (!port.StartsWith(PortId, StringComparison.OrdinalIgnoreCase))
            {
                InterfaceDisconnect();
                return false;
            }
            try
            {
                string portData = port.Remove(0, PortId.Length);
                if ((portData.Length > 0) && (portData[0] == ':'))
                {
                    // special id
                    string portName = portData.Remove(0, 1);
                    SerialPort.PortName = portName;
                    SerialPort.BaudRate = 115200;
                    SerialPort.DataBits = 8;
                    SerialPort.Parity = System.IO.Ports.Parity.None;
                    SerialPort.StopBits = System.IO.Ports.StopBits.One;
                    SerialPort.Handshake = System.IO.Ports.Handshake.None;
                    SerialPort.DtrEnable = false;
                    SerialPort.RtsEnable = false;
                    SerialPort.ReadTimeout = 1;
                    SerialPort.Open();
                }
                else
                {
                    InterfaceDisconnect();
                    return false;
                }
            }
            catch (Exception)
            {
                InterfaceDisconnect();
                return false;
            }
            return true;
        }

        public static bool InterfaceDisconnect()
        {
            try
            {
                if (SerialPort.IsOpen)
                {
                    SerialPort.Close();
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public static EdInterfaceObd.InterfaceErrorResult InterfaceSetConfig(int baudRate, int dataBits,
            EdInterfaceObd.SerialParity parity, bool allowBitBang)
        {
            if (!SerialPort.IsOpen)
            {
                return EdInterfaceObd.InterfaceErrorResult.ConfigError;
            }
            CurrentBaudRate = baudRate;
            CurrentWordLength = dataBits;
            CurrentParity = parity;
            FastInit = false;
            return EdInterfaceObd.InterfaceErrorResult.NoError;
        }

        public static bool InterfaceSetDtr(bool dtr)
        {
            if (!SerialPort.IsOpen)
            {
                return false;
            }
            return true;
        }

        public static bool InterfaceSetRts(bool rts)
        {
            if (!SerialPort.IsOpen)
            {
                return false;
            }
            return true;
        }

        public static bool InterfaceGetDsr(out bool dsr)
        {
            dsr = true;
            if (!SerialPort.IsOpen)
            {
                return false;
            }
            return true;
        }

        public static bool InterfaceSetBreak(bool enable)
        {
            return false;
        }

        public static bool InterfaceSetInterByteTime(int time)
        {
            InterByteTime = time;
            return true;
        }

        public static bool InterfacePurgeInBuffer()
        {
            if (!SerialPort.IsOpen)
            {
                return false;
            }
            try
            {
                SerialPort.DiscardInBuffer();
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public static bool InterfaceAdapterEcho()
        {
            return false;
        }

        public static bool InterfaceHasPreciseTimeout()
        {
            return false;
        }

        public static bool InterfaceSendData(byte[] sendData, int length, bool setDtr, double dtrTimeCorr)
        {
            if (!SerialPort.IsOpen)
            {
                return false;
            }
            try
            {
                if (CurrentBaudRate == 115200)
                {
                    // BMW-FAST
                    SerialPort.Write(sendData, 0, length);
                    // remove echo
                    byte[] receiveData = new byte[length];
                    if (!InterfaceReceiveData(receiveData, 0, length, EchoTimeout, EchoTimeout, null))
                    {
                        return false;
                    }
                    for (int i = 0; i < length; i++)
                    {
                        if (receiveData[i] != sendData[i])
                        {
                            return false;
                        }
                    }
                }
                else
                {
                    UpdateAdapterInfo();
                    byte[] adapterTel = CreateAdapterTelegram(sendData, length, setDtr);
                    FastInit = false;
                    if (adapterTel == null)
                    {
                        return false;
                    }
                    SerialPort.Write(adapterTel, 0, adapterTel.Length);
                    UpdateActiveSettings();
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public static bool InterfaceReceiveData(byte[] receiveData, int offset, int length, int timeout,
            int timeoutTelEnd, EdiabasNet ediabasLog)
        {
            if (!SerialPort.IsOpen)
            {
                return false;
            }
            timeout += ReadTimeoutOffset;
            timeoutTelEnd += ReadTimeoutOffset;
            try
            {
                if (SettingsUpdateRequired())
                {
                    UpdateAdapterInfo();
                    byte[] adapterTel = CreatePulseTelegram(0, 0, 0, false);
                    if (adapterTel == null)
                    {
                        return false;
                    }
                    SerialPort.Write(adapterTel, 0, adapterTel.Length);
                    UpdateActiveSettings();
                }

                // wait for first byte
                int lastBytesToRead;
                StopWatch.Reset();
                StopWatch.Start();
                for (; ; )
                {
                    lastBytesToRead = SerialPort.BytesToRead;
                    if (lastBytesToRead > 0)
                    {
                        break;
                    }
                    if (StopWatch.ElapsedMilliseconds > timeout)
                    {
                        StopWatch.Stop();
                        return false;
                    }
                    CommReceiveEvent.WaitOne(1, false);
                }

                int recLen = 0;
                StopWatch.Reset();
                StopWatch.Start();
                for (; ; )
                {
                    int bytesToRead = SerialPort.BytesToRead;
                    if (bytesToRead >= length)
                    {
                        recLen += SerialPort.Read(receiveData, offset + recLen, length - recLen);
                    }
                    if (recLen >= length)
                    {
                        break;
                    }
                    if (lastBytesToRead != bytesToRead)
                    {   // bytes received
                        StopWatch.Reset();
                        StopWatch.Start();
                        lastBytesToRead = bytesToRead;
                    }
                    else
                    {
                        if (StopWatch.ElapsedMilliseconds > timeoutTelEnd)
                        {
                            break;
                        }
                    }
                    CommReceiveEvent.WaitOne(1, false);
                }
                StopWatch.Stop();
                if (ediabasLog != null)
                {
                    ediabasLog.LogData(EdiabasNet.EdLogLevel.Ifh, receiveData, offset, recLen, "Rec ");
                }
                if (recLen < length)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        public static bool InterfaceSendPulse(UInt64 dataBits, int length, int pulseWidth, bool setDtr)
        {
            if (!SerialPort.IsOpen)
            {
                return false;
            }
            try
            {
                UpdateAdapterInfo();
                FastInit = IsFastInit(dataBits, length, pulseWidth);
                if (FastInit)
                {
                    // send next telegram with fast init
                    return true;
                }
                byte[] adapterTel = CreatePulseTelegram(dataBits, length, pulseWidth, setDtr);
                if (adapterTel == null)
                {
                    return false;
                }
                SerialPort.Write(adapterTel, 0, adapterTel.Length);
                UpdateActiveSettings();
            }
            catch (Exception)
            {
                return false;
            }
            return true;
        }

        private static void SerialDataReceived(object sender, System.IO.Ports.SerialDataReceivedEventArgs e)
        {
            CommReceiveEvent.Set();
        }

        // ReSharper disable once UnusedMethodReturnValue.Local
        private static bool UpdateAdapterInfo(bool forceUpdate = false)
        {
            if (!SerialPort.IsOpen)
            {
                return false;
            }
            if (!forceUpdate && AdapterType >= 0)
            {
                // only read once
                return true;
            }
            AdapterType = -1;
            try
            {
                const int versionRespLen = 9;
                byte[] identTel = {0x82, 0xF1, 0xF1, 0xFD, 0xFD, 0x5E};
                SerialPort.DiscardInBuffer();
                SerialPort.Write(identTel, 0, identTel.Length);

                List<byte> responseList = new List<byte>();
                long startTime = Stopwatch.GetTimestamp();
                for (;;)
                {
                    while (SerialPort.BytesToRead > 0)
                    {
                        int data = SerialPort.ReadByte();
                        if (data >= 0)
                        {
                            responseList.Add((byte) data);
                            startTime = Stopwatch.GetTimestamp();
                        }
                    }
                    if (responseList.Count >= identTel.Length + versionRespLen)
                    {
                        bool validEcho = !identTel.Where((t, i) => responseList[i] != t).Any();
                        if (!validEcho)
                        {
                            return false;
                        }
                        if (CalcChecksumBmwFast(responseList.ToArray(), identTel.Length, versionRespLen - 1) !=
                            responseList[identTel.Length + versionRespLen - 1])
                        {
                            return false;
                        }
                        AdapterType = responseList[identTel.Length + 5] + (responseList[identTel.Length + 4] << 8);
                        AdapterVersion = responseList[identTel.Length + 7] + (responseList[identTel.Length + 6] << 8);
                        break;
                    }
                    if (Stopwatch.GetTimestamp() - startTime > ReadTimeoutOffset*TickResolMs)
                    {
                        if (responseList.Count >= identTel.Length)
                        {
                            bool validEcho = !identTel.Where((t, i) => responseList[i] != t).Any();
                            if (validEcho)
                            {
                                AdapterType = 0;
                            }
                        }
                        return false;
                    }
                }
            }
            catch (Exception)
            {
                return false;
            }

            return true;
        }
    }
}
