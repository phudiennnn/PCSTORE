import { useEffect, useState } from 'react';
import QRCode from 'qrcode';
import { Loader2, Printer, X } from 'lucide-react';
import { orderService, getApiErrorMessage } from '../services/api';

const DOCUMENT_TITLES = {
  'packing-slip': 'Phiếu đóng gói',
  invoice: 'Hóa đơn bán hàng',
};

const formatMoney = value => `${Number(value || 0).toLocaleString('vi-VN')} đ`;

export default function OrderDocumentPreview({ orderId, documentType, staffName, onClose }) {
  const [order, setOrder] = useState(null);
  const [qrCode, setQrCode] = useState('');
  const [paperSize, setPaperSize] = useState('A4');
  const [loading, setLoading] = useState(true);
  const [printing, setPrinting] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    let active = true;
    const loadOrder = async () => {
      setLoading(true);
      setError('');
      try {
        const data = await orderService.getOrderById(orderId);
        const lookupUrl = new URL(window.location.origin);
        lookupUrl.searchParams.set('orderCode', data.orderCode);
        const image = await QRCode.toDataURL(lookupUrl.toString(), {
          errorCorrectionLevel: 'M',
          margin: 1,
          width: 160,
        });
        if (active) {
          setOrder(data);
          setQrCode(image);
        }
      } catch (loadError) {
        if (active) setError(getApiErrorMessage(loadError, 'Không thể tải dữ liệu chứng từ.'));
      } finally {
        if (active) setLoading(false);
      }
    };
    void loadOrder();
    return () => {
      active = false;
    };
  }, [orderId]);

  const handlePrint = async () => {
    if (!order) return;
    setPrinting(true);
    setError('');
    try {
      await orderService.recordDocumentPrint(order.id, documentType, staffName);
      window.print();
    } catch (printError) {
      setError(getApiErrorMessage(printError, 'Không thể ghi nhận yêu cầu in chứng từ.'));
    } finally {
      setPrinting(false);
    }
  };

  const items = order?.items || [];
  const subtotal = items.reduce((sum, item) => sum + Number(item.subtotal ?? item.unitPrice * item.quantity), 0);
  const shippingFee = Number(order?.shippingFee || 0);
  const totalAdjustment = subtotal + shippingFee - Number(order?.totalAmount || 0);

  return (
    <div className="order-document-overlay fixed inset-0 z-[100] flex items-center justify-center bg-black/70 p-2 sm:p-5">
      <div className="flex max-h-[96vh] w-full max-w-5xl flex-col overflow-hidden rounded-2xl bg-slate-100 shadow-2xl">
        <div className="order-document-toolbar flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-white p-3 sm:px-5">
          <div>
            <h2 className="text-sm font-black text-slate-900">{DOCUMENT_TITLES[documentType]}</h2>
            <p className="text-[11px] text-slate-500">Xem trước chứng từ đơn {order?.orderCode || ''}</p>
          </div>
          <div className="flex items-center gap-2">
            <label className="flex items-center gap-2 text-xs font-semibold text-slate-600">
              Khổ giấy
              <select
                value={paperSize}
                onChange={event => setPaperSize(event.target.value)}
                className="rounded-lg border border-slate-300 bg-white px-2.5 py-2"
              >
                <option value="A4">A4</option>
                <option value="A5">A5</option>
              </select>
            </label>
            <button
              type="button"
              onClick={handlePrint}
              disabled={loading || printing || !order}
              className="flex items-center gap-1.5 rounded-lg bg-blue-600 px-3 py-2 text-xs font-bold text-white hover:bg-blue-700 disabled:cursor-wait disabled:opacity-50"
            >
              {printing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Printer className="h-4 w-4" />}
              {printing ? 'Đang ghi nhận...' : 'In / Lưu PDF'}
            </button>
            <button type="button" onClick={onClose} aria-label="Đóng xem trước" className="rounded-lg p-2 text-slate-500 hover:bg-slate-100">
              <X className="h-4 w-4" />
            </button>
          </div>
        </div>

        {error && <p role="alert" className="order-document-toolbar mx-4 mt-3 rounded-lg border border-red-200 bg-red-50 p-3 text-xs text-red-700">{error}</p>}

        <div className="min-h-0 overflow-auto p-3 sm:p-6">
          {loading ? (
            <div className="flex min-h-64 items-center justify-center gap-2 text-sm text-slate-500">
              <Loader2 className="h-5 w-5 animate-spin" /> Đang tải chứng từ...
            </div>
          ) : order ? (
            <article
              className={`order-document-print mx-auto bg-white p-6 text-slate-900 shadow-lg sm:p-10 ${paperSize === 'A4' ? 'order-document-a4' : 'order-document-a5'}`}
              style={{ width: paperSize === 'A4' ? '210mm' : '148mm', minHeight: paperSize === 'A4' ? '297mm' : '210mm' }}
            >
              <header className="flex items-start justify-between gap-4 border-b-2 border-slate-900 pb-4">
                <div>
                  <p className="text-lg font-black tracking-wide">PC STORE</p>
                  <p className="mt-1 text-[10px] text-slate-500">Chứng từ bán hàng và xử lý đơn hàng</p>
                </div>
                <div className="text-right">
                  <h1 className="text-xl font-black uppercase">{DOCUMENT_TITLES[documentType]}</h1>
                  <p className="mt-1 text-xs">Mã đơn: <strong>{order.orderCode}</strong></p>
                  <p className="text-[10px] text-slate-500">Ngày tạo: {new Date(order.createdAt).toLocaleString('vi-VN')}</p>
                </div>
              </header>

              <section className="grid grid-cols-2 gap-x-6 gap-y-2 py-4 text-xs">
                <p><strong>Người nhận:</strong> {order.receiverName}</p>
                <p><strong>Điện thoại:</strong> {order.receiverPhone}</p>
                <p className="col-span-2"><strong>Địa chỉ:</strong> {order.shippingAddress}</p>
                <p><strong>Thanh toán:</strong> {order.paymentMethod === 'COD' ? 'COD - thanh toán khi nhận hàng' : order.paymentMethod}</p>
                <p><strong>Trạng thái:</strong> {order.status}</p>
                {order.voucherCode && <p><strong>Voucher:</strong> {order.voucherCode} (-{formatMoney(order.discountAmount || 0)})</p>}
                {order.carrier && <p><strong>Đơn vị vận chuyển:</strong> {order.carrier}</p>}
                {order.trackingNumber && <p><strong>Mã vận đơn:</strong> {order.trackingNumber}</p>}
              </section>

              {documentType === 'packing-slip' && (
                <div className="mb-3 grid grid-cols-2 gap-2 rounded border border-slate-300 p-2 text-[10px]">
                  <p><strong>Mã cấu hình PC:</strong> {order.buildCode || 'Không có liên kết cấu hình PC với đơn hàng này'}</p>
                  <p><strong>Nhân viên xuất phiếu:</strong> {staffName}</p>
                </div>
              )}

              <table className="w-full border-collapse text-left text-xs">
                <thead>
                  <tr className="border-y border-slate-500 bg-slate-50">
                    <th className="p-2">#</th>
                    <th className="p-2">SKU / Part Number</th>
                    <th className="p-2">Tên linh kiện</th>
                    {documentType === 'packing-slip' ? (
                      <>
                        <th className="p-2">Vị trí kho</th>
                        <th className="p-2 text-right">Số lượng</th>
                      </>
                    ) : (
                      <>
                        <th className="p-2 text-right">Đơn giá</th>
                        <th className="p-2 text-right">SL</th>
                        <th className="p-2 text-right">Thành tiền</th>
                      </>
                    )}
                  </tr>
                </thead>
                <tbody>
                  {items.map((item, index) => (
                    <tr key={`${item.productId}-${index}`} className="border-b border-slate-200">
                      <td className="p-2">{index + 1}</td>
                      <td className="p-2 font-mono">{item.sku || `SP-${item.productId}`}</td>
                      <td className="p-2">{item.productName}</td>
                      {documentType === 'packing-slip' ? (
                        <>
                          <td className="p-2">{item.warehouseLocation || 'Chưa gán vị trí'}</td>
                          <td className="p-2 text-right font-bold">{item.quantity}</td>
                        </>
                      ) : (
                        <>
                          <td className="p-2 text-right">{formatMoney(item.unitPrice)}</td>
                          <td className="p-2 text-right">{item.quantity}</td>
                          <td className="p-2 text-right">{formatMoney(item.subtotal ?? item.unitPrice * item.quantity)}</td>
                        </>
                      )}
                    </tr>
                  ))}
                </tbody>
              </table>

              {documentType === 'invoice' && (
                <div className="ml-auto mt-4 max-w-xs space-y-1 text-right text-xs">
                  <p className="flex justify-between gap-6"><span>Tiền hàng</span><strong>{formatMoney(subtotal)}</strong></p>
                  <p className="flex justify-between gap-6"><span>Phí vận chuyển</span><strong>{formatMoney(shippingFee)}</strong></p>
                  <p className="flex justify-between gap-6">
                    <span>{totalAdjustment >= 0 ? 'Giảm giá / voucher' : 'Phụ thu'}</span>
                    <strong>{totalAdjustment >= 0 ? `-${formatMoney(totalAdjustment)}` : formatMoney(Math.abs(totalAdjustment))}</strong>
                  </p>
                  <p className="flex justify-between gap-6 border-t border-slate-500 pt-2 text-sm"><span className="font-black">Tổng thanh toán</span><strong>{formatMoney(order.totalAmount)}</strong></p>
                  <p className="pt-1 text-[10px] text-slate-500">{order.isPaid ? 'Đã thanh toán' : 'Chưa thanh toán'}</p>
                </div>
              )}

              <footer className="mt-8 flex items-end justify-between gap-6 text-center text-[10px]">
                <div className="max-w-28">
                  {qrCode && <img src={qrCode} alt={`QR tra cứu đơn ${order.orderCode}`} className="mx-auto h-20 w-20" />}
                  <p className="mt-1">QR tra cứu mã đơn</p>
                  <p className="font-mono">{order.orderCode}</p>
                </div>
                <div className="flex flex-1 justify-around gap-6">
                  <div><p className="font-bold">Nhân viên</p><p className="mt-12">{staffName}</p></div>
                  <div><p className="font-bold">Người nhận hàng</p><p className="mt-12">Ký và ghi rõ họ tên</p></div>
                </div>
              </footer>
            </article>
          ) : null}
        </div>
      </div>

      <style>{`
        @media print {
          @page { size: ${paperSize} portrait; margin: 12mm; }
          body * { visibility: hidden !important; }
          .order-document-overlay { position: static !important; display: block !important; padding: 0 !important; background: white !important; }
          .order-document-overlay > div { display: block !important; width: auto !important; max-width: none !important; max-height: none !important; overflow: visible !important; background: white !important; box-shadow: none !important; }
          .order-document-toolbar { display: none !important; }
          .order-document-overlay .overflow-auto { overflow: visible !important; padding: 0 !important; }
          .order-document-print, .order-document-print * { visibility: visible !important; }
          .order-document-print { position: absolute !important; left: 0 !important; top: 0 !important; width: 100% !important; min-height: 0 !important; margin: 0 !important; padding: 0 !important; box-shadow: none !important; }
          .order-document-a5 { font-size: 9pt !important; }
          .order-document-a5 h1 { font-size: 14pt !important; }
          .order-document-a5 table { font-size: 8pt !important; }
          tr { break-inside: avoid; }
        }
      `}</style>
    </div>
  );
}
