namespace SysAdminAssistant.Core;
public sealed class InvoiceConflictException(string message):InvalidOperationException(message);
