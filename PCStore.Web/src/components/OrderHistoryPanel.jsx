import { useEffect, useState } from 'react';
import { ClipboardList, Loader2, X } from 'lucide-react';
import { getApiErrorMessage, orderService } from '../services/api';

const STATUS_LABELS = {
  Pending: 'Chờ xử lý',
  Confirmed: 'Đã xác nhận / Đang đóng gói',
  Shipping: 'Đang giao hàng',
  Completed: 'Đã giao hàng thành công',
  Cancelled: 'Đã hủy đơn',
  DeliveryFailed: 'Giao hàng thất bại',
  Refunded: 'Đã hoàn tiền',
};

export default function OrderHistoryPanel({ userId, onClose }) {
  const [orders, setOrders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [requestDraft, setRequestDraft] = useState(null);
  const [requestLoadingId, setRequestLoadingId] = useState(null);
  const [requestMessage, setRequestMessage] = useState('');

  useEffect(() => {
    let active = true;
    const loadOrders = async () => {
      try {
        const result = await orderService.getUserOrders(userId);
        if (active) setOrders(result);
      } catch (loadError) {
        if (active) setError(getApiErrorMessage(loadError, 'Không thể tải đơn hàng của bạn.'));
      } finally {
        if (active) setLoading(false);
      }
    };
    void loadOrders();
    return () => {
      active = false;
    };
  }, [userId]);

  const handleSubmitRequest = async order => {
    if (!requestDraft || requestDraft.orderId !== order.id) return;
    setRequestLoadingId(order.id);
    setRequestMessage('');
    try {
      await orderService.submitOrderActionRequest(order.id, requestDraft.type, requestDraft.reason);
      setRequestDraft(null);
      setRequestMessage(`Đã gửi yêu cầu cho đơn ${order.orderCode}. Chờ Manager/Admin phê duyệt.`);
      setOrders(await orderService.getUserOrders(userId));
    } catch (requestError) {
      setRequestMessage(getApiErrorMessage(requestError, 'Không gửi được yêu cầu xử lý đơn.'));
    } finally {
      setRequestLoadingId(null);
    }
  };

  return (
    <div className="fixed inset-0 z-[100] flex items-center justify-center bg-black/70 p-3 sm:p-5">
      <section role="dialog" aria-modal="true" aria-labelledby="order-history-title" className="flex max-h-[92vh] w-full max-w-3xl flex-col overflow-hidden rounded-2xl border border-cyan-900 bg-[#0c1424] text-slate-100 shadow-2xl">
        <header className="flex items-center justify-between border-b border-cyan-950 bg-[#090e1a] p-4">
          <div className="flex items-center gap-2">
            <ClipboardList className="h-5 w-5 text-cyan-400" />
            <div>
              <h2 id="order-history-title" className="text-sm font-black">Đơn hàng của tôi</h2>
              <p className="mt-0.5 text-[10px] text-slate-400">Xem trạng thái, thông tin vận chuyển và lịch sử xử lý.</p>
            </div>
          </div>
          <button type="button" onClick={onClose} aria-label="Đóng đơn hàng" className="rounded-lg p-2 text-slate-400 hover:bg-slate-800 hover:text-white">
            <X className="h-4 w-4" />
          </button>
        </header>

        <div className="overflow-y-auto p-4">
          {loading ? (
            <div className="flex min-h-48 items-center justify-center gap-2 text-sm text-slate-400">
              <Loader2 className="h-5 w-5 animate-spin" /> Đang tải đơn hàng...
            </div>
          ) : error ? (
            <p role="alert" className="rounded-xl border border-rose-900 bg-rose-950/40 p-3 text-xs text-rose-300">{error}</p>
          ) : orders.length === 0 ? (
            <div className="py-12 text-center text-sm text-slate-400">Bạn chưa có đơn hàng nào.</div>
          ) : (
            <div className="space-y-3">
              {orders.map(order => (
                <article key={order.id} className="rounded-xl border border-cyan-950 bg-[#111a2e] p-3 sm:p-4">
                  <div className="flex flex-wrap items-start justify-between gap-2">
                    <div>
                      <p className="font-bold text-cyan-300">{order.orderCode}</p>
                      <p className="mt-1 text-[10px] text-slate-400">{new Date(order.createdAt).toLocaleString('vi-VN')}</p>
                    </div>
                    <span className="rounded-full bg-cyan-950 px-2.5 py-1 text-[10px] font-bold text-cyan-200">{STATUS_LABELS[order.status] || order.status}</span>
                  </div>

                  <div className="mt-3 space-y-1 text-xs">
                    {order.items?.map((item, index) => (
                      <div key={`${item.productId}-${index}`} className="flex justify-between gap-3 text-slate-300">
                        <span>{item.productName} × {item.quantity}</span>
                        <span className="shrink-0">{(item.unitPrice * item.quantity).toLocaleString('vi-VN')} đ</span>
                      </div>
                    ))}
                  </div>

                  <div className="mt-3 flex flex-wrap items-end justify-between gap-2 border-t border-cyan-950 pt-3">
                    <div className="text-[10px] text-slate-400">
                      {order.voucherCode && <p>Voucher: <span className="font-semibold text-emerald-300">{order.voucherCode}</span> (-{Number(order.discountAmount || 0).toLocaleString('vi-VN')} đ)</p>}
                      {order.carrier && <p>Đơn vị vận chuyển: {order.carrier}</p>}
                      {order.trackingNumber && <p>Mã vận đơn: <span className="font-mono text-cyan-300">{order.trackingNumber}</span></p>}
                      {!order.trackingNumber && order.status === 'Confirmed' && <p>Đơn hàng đang được chuẩn bị.</p>}
                    </div>
                    <p className="text-sm font-black text-white">{Number(order.totalAmount).toLocaleString('vi-VN')} đ</p>
                  </div>

                  {order.statusHistory?.length > 0 && (
                    <ol className="mt-3 space-y-2 border-l border-cyan-900 pl-3">
                      {order.statusHistory.map((entry, index) => (
                        <li key={`${entry.status}-${entry.changedAt}-${index}`} className="relative text-[10px]">
                          <span className="absolute -left-[17px] top-1 h-2 w-2 rounded-full bg-cyan-500" />
                          <p className="font-bold text-slate-300">{STATUS_LABELS[entry.status] || entry.status}</p>
                          <p className="text-slate-500">{new Date(entry.changedAt).toLocaleString('vi-VN')}</p>
                          {entry.note && <p className="mt-0.5 text-slate-400">{entry.note}</p>}
                        </li>
                      ))}
                    </ol>
                  )}

                  {order.actionRequests?.length > 0 && (
                    <div className="mt-3 space-y-1 border-t border-cyan-950 pt-3">
                      {order.actionRequests.map((actionRequest, index) => (
                        <div key={`${actionRequest.requestType}-${actionRequest.requestedAt}-${index}`} className="rounded-lg bg-[#0c1424] p-2 text-[10px]">
                          <p className="font-bold text-cyan-300">
                            {actionRequest.requestType === 'Cancel' ? 'Yêu cầu hủy đơn' : 'Yêu cầu hoàn trả linh kiện'}
                            {' · '}
                            {actionRequest.status === 'Pending' ? 'Đang chờ duyệt' : actionRequest.status === 'Approved' ? 'Đã phê duyệt' : 'Đã từ chối'}
                          </p>
                          <p className="mt-0.5 text-slate-400">Lý do: {actionRequest.reason}</p>
                          {actionRequest.reviewNote && <p className="mt-0.5 text-slate-400">Phản hồi: {actionRequest.reviewNote}</p>}
                        </div>
                      ))}
                    </div>
                  )}

                  {(() => {
                    const hasPendingRequest = order.actionRequests?.some(actionRequest => actionRequest.status === 'Pending');
                    const requestType = ['Pending', 'Confirmed'].includes(order.status)
                      ? 'Cancel'
                      : order.status === 'Completed' ? 'Refund' : null;
                    if (!requestType || hasPendingRequest) return null;
                    const isEditing = requestDraft?.orderId === order.id;
                    return (
                      <div className="mt-3 border-t border-cyan-950 pt-3">
                        {!isEditing ? (
                          <button
                            type="button"
                            onClick={() => setRequestDraft({ orderId: order.id, type: requestType, reason: '' })}
                            className="rounded-lg border border-amber-800 px-3 py-2 text-[10px] font-bold text-amber-300 hover:bg-amber-950"
                          >
                            {requestType === 'Cancel' ? 'Yêu cầu hủy đơn' : 'Yêu cầu hoàn trả linh kiện'}
                          </button>
                        ) : (
                          <div className="space-y-2">
                            <label className="block text-[10px] font-bold text-slate-300" htmlFor={`order-request-${order.id}`}>
                              {requestType === 'Cancel' ? 'Lý do hủy đơn' : 'Lý do hoàn trả linh kiện'}
                            </label>
                            <textarea
                              id={`order-request-${order.id}`}
                              maxLength={500}
                              rows={2}
                              required
                              value={requestDraft.reason}
                              onChange={event => setRequestDraft(draft => ({ ...draft, reason: event.target.value }))}
                              className="w-full rounded-lg border border-cyan-900 bg-[#0c1424] px-3 py-2 text-xs text-white"
                            />
                            <div className="flex gap-2">
                              <button type="button" onClick={() => setRequestDraft(null)} className="rounded-lg border border-cyan-900 px-3 py-2 text-[10px] text-slate-300">Đóng</button>
                              <button
                                type="button"
                                disabled={!requestDraft.reason.trim() || requestLoadingId === order.id}
                                onClick={() => handleSubmitRequest(order)}
                                className="rounded-lg bg-amber-700 px-3 py-2 text-[10px] font-bold text-white disabled:opacity-50"
                              >
                                {requestLoadingId === order.id ? 'Đang gửi...' : 'Gửi Manager/Admin phê duyệt'}
                              </button>
                            </div>
                          </div>
                        )}
                      </div>
                    );
                  })()}
                </article>
              ))}
            </div>
          )}
          {requestMessage && <p role="status" className="mt-3 rounded-lg border border-cyan-900 bg-cyan-950/30 p-3 text-xs text-cyan-200">{requestMessage}</p>}
        </div>
      </section>
    </div>
  );
}
