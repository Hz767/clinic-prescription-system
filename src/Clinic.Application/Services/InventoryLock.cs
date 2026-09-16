namespace Clinic.Application.Services;

/// <summary>
/// 全应用共享的库存互斥锁。
/// 所有库存增减操作（入库 StockIn、手动出库 StockOut、发药扣减 DeductInventory、
/// 作废回退 ReverseInventory、退费回退 RestoreInventory）统一串行，
/// 防止并发读写同一 DrugStock 批次导致库存不一致或超卖。
/// 不同服务（InventoryService / PrescriptionService / BillingService）
/// 必须共用本锁才能真正互斥，避免各自持有独立锁造成的竞态窗口。
/// </summary>
public static class InventoryLock
{
    /// <summary>进程级库存互斥信号量（容量 1，一次仅允许一个库存变更操作执行）</summary>
    public static readonly SemaphoreSlim Instance = new(1, 1);
}