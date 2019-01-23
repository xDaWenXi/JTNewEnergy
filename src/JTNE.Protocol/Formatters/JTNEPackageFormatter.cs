﻿using JTNE.Protocol.Enums;
using JTNE.Protocol.Exceptions;
using JTNE.Protocol.Extensions;
using JTNE.Protocol.Internal;
using System;
using System.Collections.Generic;
using System.Text;

namespace JTNE.Protocol.Formatters
{
    public class JTNEPackageFormatter : IJTNEFormatter<JTNEPackage>
    {
        private const byte FixedDataBodyLength = 2;

        public JTNEPackage Deserialize(ReadOnlySpan<byte> bytes, out int readSize)
        {
            int offset = 0;
            // 1.进行固定头校验
            if (bytes[offset] != JTNEPackage.BeginFlag && bytes[offset + 1] == JTNEPackage.BeginFlag)
                throw new JTNEException(JTNEErrorCode.BeginFlagError, $"{bytes[offset]},{bytes[offset + 1]}");
            // 2.进行BCC校验码
            // 校验位 = 报文长度 - 最后一位（校验位）
            if (!JTNEGlobalConfigs.Instance.SkipCRCCode)
            {
                byte bCCCode = bytes[bytes.Length - 1];
                byte bCCCode2 = bytes.ToXor(2, bytes.Length - 1);
                if (bCCCode != bCCCode2)
                    throw new JTNEException(JTNEErrorCode.BCCCodeError, $"request:{bCCCode}!=calculate:{bCCCode2}");
            }
            JTNEPackage jTNEPackage = new JTNEPackage();
            offset += 2;
            // 3.命令标识
            jTNEPackage.MsgId = JTNEBinaryExtensions.ReadByteLittle(bytes, ref offset);
            // 4.应答标识
            jTNEPackage.AskId = JTNEBinaryExtensions.ReadByteLittle(bytes, ref offset);
            // 5.VIN
            jTNEPackage.VIN = JTNEBinaryExtensions.ReadStringLittle(bytes, ref offset, 17);
            // 6.数据加密方式
            jTNEPackage.EncryptMethod = JTNEBinaryExtensions.ReadByteLittle(bytes, ref offset);
            // 7.数据单元长度是数据单元的总字节数
            jTNEPackage.DataUnitLength = JTNEBinaryExtensions.ReadUInt16Little(bytes, ref offset);
            // 8.数据体
            // 8.1.根据数据加密方式进行解码
            // todo: 8.2.解析出对应数据体
            if (jTNEPackage.DataUnitLength > 0)
            {
                Type jTNEBodiesImplType = JTNEMsgIdFactory.GetBodiesImplTypeByMsgId(jTNEPackage.MsgId);
                if (jTNEBodiesImplType != null)
                {
                    int bodyReadSize = 0;
                    try
                    {
                        jTNEPackage.Bodies = JTNEFormatterResolverExtensions.JTNEDynamicDeserialize(
                            JTNEFormatterExtensions.GetFormatter(jTNEBodiesImplType),
                            bytes.Slice(offset, jTNEPackage.DataUnitLength),
                            out bodyReadSize);
                    }
                    catch (Exception ex)
                    {
                        throw new JTNEException(JTNEErrorCode.BodiesParseError, ex);
                    }
                    offset += bodyReadSize;
                }
            }
            // 9.校验码
            jTNEPackage.BCCCode = JTNEBinaryExtensions.ReadByteLittle(bytes, ref offset);
            readSize = offset;
            return jTNEPackage;
        }

        public int Serialize(ref byte[] bytes, int offset, JTNEPackage value)
        {
            // 1.起始符1
            offset += JTNEBinaryExtensions.WriteByteLittle(bytes, offset, value.BeginFlag1);
            // 2.起始符2
            offset += JTNEBinaryExtensions.WriteByteLittle(bytes, offset, value.BeginFlag2);
            // 3.命令标识
            offset += JTNEBinaryExtensions.WriteByteLittle(bytes, offset, value.MsgId);
            // 4.应答标识
            offset += JTNEBinaryExtensions.WriteByteLittle(bytes, offset, value.AskId);
            // 5.VIN
            offset += JTNEBinaryExtensions.WriteStringPadRightLittle(bytes, offset, value.VIN, 17);
            // 6.数据加密方式
            offset += JTNEBinaryExtensions.WriteByteLittle(bytes, offset, value.EncryptMethod);
            // 7.记录存储数据长度的当前偏移量
            int tmpOffset = offset;
            offset += FixedDataBodyLength;
            // 8.数据体
            Type jTNEBodiesImplType = JTNEMsgIdFactory.GetBodiesImplTypeByMsgId(value.MsgId);
            int messageBodyOffset = 0;
            if (jTNEBodiesImplType != null)
            {
                if (value.Bodies != null)
                {
                    // 8.1 处理数据体
                    // todo: 8.2.判断是否有加密
                    messageBodyOffset = JTNEFormatterResolverExtensions.JTNEDynamicSerialize(
                        JTNEFormatterExtensions.GetFormatter(jTNEBodiesImplType),
                        ref bytes,
                        offset,
                        value.Bodies);
                    // 9.通过tmpOffset反写数据单元长度
                    JTNEBinaryExtensions.WriteUInt16Little(bytes, tmpOffset, (ushort)(messageBodyOffset - offset));
                    offset = messageBodyOffset;
                }
            }
            // 10.校验码
            var bccCode = bytes.ToXor(2, offset);
            offset += JTNEBinaryExtensions.WriteByteLittle(bytes, offset, bccCode);
            return offset;
        }
    }
}
