import { useState } from 'react';
import { Loader2, Search, X } from 'lucide-react';
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

export default function OrderLookupModal({ initialOrderCode = '', onClose }) {
  const [orderCode, setOrderCode] = useState(initialOrderCode);
  const [receiverPhone, setReceiverPhone] = useState('');
  const [order, setOrder] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  const handleSubmit = async event => {
    event.preventDefault();
    setLoading(true);
    setError('');
    setOrder(null);
    try {
      setOrder(await orderService.lookupOrder(orderCode.trim(), receiverPhone.trim()));
    } catch (lookupError) {
      setError(getApiErrorMessage(lookupError, 'Không thể tra cứu đơn hàng.'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 z-[110] flex items-center justify-center bg-black/70 p-4">
      <section role="dialog" aria-modal="true" aria-labelledby="order-lookup-title" className="w-full max-w-xl overflow-hidden rounded-2xl bg-white shadow-2xl">
        <header className="flex items-center justify-between border-b border-slate-200 p-4">
          <div>
            <h2 id="order-lookup-title" className="text-sm font-black text-slate-900">Tra cứu đơn hàng</h2>
            <p className="mt-1 text-[11px] text-slate-500">Nhập mã đơn và số điện thoại người nhận để xác minh.</p>
          </div>
          <button type="button" onClick={onClose} aria-label="Đóng tra cứu" className="rounded-lg p-2 text-slate-500 hover:bg-slate-100">
            <X className="h-4 w-4" />
          </button>
        </header>

        <form onSubmit={handleSubmit} className="grid gap-3 p-4 sm:grid-cols-2">
          <label className="text-xs font-bold text-slate-600">
            Mã đơn hàng
            <input
              value={orderCode}
              onChange={event => setOrderCode(event.target.value)}
              required
              className="mt-1.5 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm font-normal text-slate-900"
            />
          </label>
          <label className="text-xs font-bold text-slate-600">
            Điện thoại người nhận
            <input
              type="tel"
              value={receiverPhone}
              onChange={event => setReceiverPhone(event.target.value)}
              required
              className="mt-1.5 w-full rounded-lg border border-slate-300 px-3 py-2 text-sm font-normal text-slate-900"
            />
          </label>
          <button type="submit" disabled={loading} className="flex items-center justify-center gap-2 rounded-lg bg-blue-600 px-4 py-2.5 text-xs font-bold text-white hover:bg-blue-700 disabled:opacity-50 sm:col-span-2">
            {loading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
            {loading ? 'Đang tra cứu...' : 'Tra cứu'}
          </button>
        </form>

        {error && <p role="alert" className="mx-4 mb-4 rounded-lg border border-red-200 bg-red-50 p-3 text-xs text-red-700">{error}</p>}

        {order && (
          <div className="border-t border-slate-200 p-4">
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p className="text-sm font-black text-slate-900">{order.orderCode}</p>
              <span className="rounded-full bg-blue-50 px-3 py-1 text-xs font-bold text-blue-700">{STATUS_LABELS[order.status] || order.status}</span>
            </div>
            {(order.carrier || order.trackingNumber) && (
              <p className="mt-2 text-xs text-slate-600">
                {order.carrier || 'Đơn vị vận chuyển'}{order.trackingNumber ? ` · Mã vận đơn: ${order.trackingNumber}` : ''}
              </p>
            )}
            <ol className="mt-4 max-h-56 space-y-3 overflow-auto border-l-2 border-blue-100 pl-4">
              {order.statusHistory.map((entry, index) => (
                <li key={`${entry.status}-${entry.changedAt}-${index}`} className="relative text-xs">
                  <span className="absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full bg-blue-500" />
                  <p className="font-bold text-slate-800">{STATUS_LABELS[entry.status] || entry.status}</p>
                  <p className="mt-0.5 text-[10px] text-slate-500">{new Date(entry.changedAt).toLocaleString('vi-VN')}</p>
                  {entry.note && <p className="mt-1 text-[10px] text-slate-600">{entry.note}</p>}
                </li>
              ))}
            </ol>
          </div>
        )}
      </section>
    </div>
  );
}
