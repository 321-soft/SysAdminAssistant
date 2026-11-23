namespace SysAdminAssistant.Server;
internal sealed class LimitedInvoiceStream(int limit):MemoryStream
{
 void Check(int n){if(Position+n>limit)throw new InvalidDataException(global::SysAdminAssistant.Core.ProductText.Get("Вложение превышает 8 МБ"));}
 public override void Write(byte[] b,int o,int n){Check(n);base.Write(b,o,n);}public override void Write(ReadOnlySpan<byte> b){Check(b.Length);base.Write(b);}public override void WriteByte(byte b){Check(1);base.WriteByte(b);}
}
