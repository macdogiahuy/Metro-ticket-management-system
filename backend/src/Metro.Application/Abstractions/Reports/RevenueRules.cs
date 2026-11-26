using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Metro.Application.Reports;

/// <summary>
/// Quy tắc tính số liệu doanh thu theo đơn vị tiền nhỏ nhất, ví dụ VND.
/// Các payment đầu vào phải được lọc hợp lệ trước khi gọi các hàm này.
/// </summary>
public static class RevenueRules
{
    public const int MaxExportRows = 10_000;

    /// <summary>Checks the number of result rows after filtering and grouping.</summary>
    public static bool ExceedsExportLimit(int rowCount) => rowCount > MaxExportRows;

    /// <summary>
    /// Phần tiền thu được tính vào giá trị hợp lệ của order.
    /// </summary>
    public static long ValidPaidOrderValue(
        long collectedMinor,
        long frozenOrderTotalMinor)
    {
        return Math.Min(collectedMinor, frozenOrderTotalMinor);
    }

    /// <summary>
    /// Phần tiền thu vượt giá trị order version tại thời điểm thanh toán.
    /// </summary>
    public static long ExcessCollected(
        long collectedMinor,
        long frozenOrderTotalMinor)
    {
        return Math.Max(0, collectedMinor - frozenOrderTotalMinor);
    }

    /// <summary>
    /// Dòng tiền thuần trong kỳ. Refund đang chờ không trừ vào dòng tiền.
    /// </summary>
    public static long NetCashflow(
        long actualCollectedMinor,
        long successfulRefundMinor)
    {
        return actualCollectedMinor - successfulRefundMinor;
    }
}
