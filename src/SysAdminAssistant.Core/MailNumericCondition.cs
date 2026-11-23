using System.Globalization;

namespace SysAdminAssistant.Core;

public sealed record MailNumericCondition(string Line,string Anchor,bool After,string Operator,decimal Threshold,bool Any)
{
    public static bool TryNumber(string value,out decimal number)
    {
        var token=value.AsSpan().Trim();
        number=0;
        if(token.Length is 0 or >32)return false;
        if(token[0] is '+' or '-')token=token[1..];
        if(token.IsEmpty||!char.IsAsciiDigit(token[0])||!char.IsAsciiDigit(token[^1]))return false;
        return decimal.TryParse(value.Trim().Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out number);
    }

    public void Validate()
    {
        static bool ValidText(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=160&&s.IndexOfAny(['\r','\n'])<0;
        if(!ValidText(Line)||!ValidText(Anchor)||Operator is not ("<" or "<=" or ">" or ">=" or "="))
            throw new ArgumentException("Укажите фразу строки, текст рядом с числом и знак сравнения.");
    }

    public bool Matches(decimal value)=>Operator switch
    {
        "<"=>value<Threshold,"<="=>value<=Threshold,">"=>value>Threshold,">="=>value>=Threshold,"="=>value==Threshold,_=>false
    };

    // Anchors are literal and extraction cannot cross a line break.
    public (decimal? Match,string Evidence) Evaluate(string body)
    {
        var lineMarker=Line.Trim().AsSpan();
        var anchor=Anchor.Trim().AsSpan();
        if(lineMarker.IsEmpty||anchor.IsEmpty)return (null,"Числовое условие не заполнено.");
        var occurrences=0;
        decimal? single=null;
        var remaining=body.AsSpan();
        while(!remaining.IsEmpty)
        {
            var end=remaining.IndexOfAny('\r','\n');
            var line=end<0?remaining:remaining[..end];
            remaining=end<0?default:remaining[(end+1)..];
            if(line.IndexOf(lineMarker,StringComparison.OrdinalIgnoreCase)<0)continue;
            var offset=0;
            while(offset<line.Length)
            {
                var relative=line[offset..].IndexOf(anchor,StringComparison.OrdinalIgnoreCase);
                if(relative<0)break;
                var position=offset+relative;
                offset=position+anchor.Length;
                var side=After?line[offset..].TrimStart():line[..position].TrimEnd();
                var length=0;
                static bool TokenChar(char c)=>char.IsAsciiDigit(c)||c is '.' or ',' or '-' or '+';
                while(length<side.Length&&TokenChar(After?side[length]:side[^(length+1)]))length++;
                if(length==0)continue;
                var token=After?side[..length]:side[^length..];
                var rest=After?side[length..]:side[..^length];
                // Reject partial numbers, IDs and ambiguous space-separated digit groups.
                if(!rest.IsEmpty)
                {
                    var edge=After?rest[0]:rest[^1];
                    if(char.IsLetterOrDigit(edge)||edge=='_')continue;
                    if(char.IsWhiteSpace(edge))
                    {
                        var adjacent=After?rest.TrimStart():rest.TrimEnd();
                        if(!adjacent.IsEmpty&&char.IsAsciiDigit(After?adjacent[0]:adjacent[^1]))continue;
                    }
                }
                if(!TryNumber(token.ToString(),out var value))continue;
                occurrences++;
                if(Any&&Matches(value))return (value,Describe(value,true));
                if(!Any&&occurrences>1)return (null,"Найдено несколько чисел; уточните строку или разрешите любое совпадение.");
                single=value;
            }
        }
        if(single==null)return (null,"Число рядом с указанным текстом не найдено.");
        if(!Any&&Matches(single.Value))return (single,Describe(single.Value,true));
        return (null,Any&&occurrences>1?"Ни одно из найденных чисел не прошло сравнение.":Describe(single.Value,false));
    }

    private string Describe(decimal value,bool match)=>
        value.ToString(CultureInfo.InvariantCulture)+" "+Operator+" "+Threshold.ToString(CultureInfo.InvariantCulture)+
        (match?" — условие выполнено.":" — условие не выполнено.");
}
