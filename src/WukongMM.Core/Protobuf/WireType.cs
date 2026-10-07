namespace WukongMM.Core.Protobuf;

/// <summary>protobuf wire type。group (3/4) 已废弃，解析时视为非法。</summary>
public enum WireType : byte
{
    Varint = 0,
    Fixed64 = 1,
    LengthDelimited = 2,
    StartGroup = 3,
    EndGroup = 4,
    Fixed32 = 5,
}
