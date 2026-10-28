import { useState, useEffect, useCallback } from 'react';
import { Package, Users, Headphones, Loader2, X, Printer, Eye, CheckCircle2, ClipboardList, Truck, Cpu, FolderTree, Search, RefreshCw, CircleCheck, Wallet, ArrowUpRight, BriefcaseBusiness, ArrowLeft, UserRound, KeyRound, LogOut } from 'lucide-react';
import { staffService, orderService, getApiErrorMessage } from '../services/api';
import OrderDocumentPreview from './OrderDocumentPreview';
import CustomerLookup from './CustomerLookup';
import StaffCompatibilityTool from './StaffCompatibilityTool';
import ProfileModal from './ProfileModal';

const TABS = [
  { id: 'orders', label: 'Xử lý đơn', icon: Package },
  { id: 'warehouse', label: 'Phiếu xuất kho', icon: ClipboardList },
  { id: 'delivery', label: 'Đang vận chuyển', icon: Truck },
  { id: 'compatibility', label: 'Tương thích PC', icon: Cpu },
  { id: 'customers', label: 'Tra cứu KH', icon: Users },
  { id: 'support', label: 'Hỗ trợ KT', icon: Headphones },
];

const CARRIERS = ['GHN', 'GHTK', 'Viettel Post', 'J&T Express', 'VNPost', 'Khác'];
const ORDER_STATUS_LABELS = {
  Pending: 'Chờ xử lý',
  Confirmed: 'Đã xác nhận / Đang đóng gói',
  Shipping: 'Đã bàn giao vận chuyển / Đang giao hàng',
  Completed: 'Đã giao thành công',
  Cancelled: 'Đã hủy',
  DeliveryFailed: 'Giao hàng thất bại',
};

export default function StaffPanel({ onClose, staffName = 'Nhân viên', currentUser, onUserUpdated, onLogout, canLookupCustomers = false, onManageCatalog, permissions = [] }) {
  const [tab, setTab] = useState('orders');
  const [profileSection, setProfileSection] = useState(null);
  const [loading, setLoading] = useState(false);
  const [orders, setOrders] = useState([]);
  const [tickets, setTickets] = useState([]);
  const [msg, setMsg] = useState('');
  const [documentOrder, setDocumentOrder] = useState(null);
  const [expandedOrderId, setExpandedOrderId] = useState(null);
  const [confirmingOrderId, setConfirmingOrderId] = useState(null);
  const [updatingOrderId, setUpdatingOrderId] = useState(null);
  const [statusDrafts, setStatusDrafts] = useState({});
  const [orderSearch, setOrderSearch] = useState('');
  const [ticketSearch, setTicketSearch] = useState('');
  const [ticketStatusFilter, setTicketStatusFilter] = useState('');
  const [ticketFilters, setTicketFilters] = useState({ search: '', status: '' });
  const [updatingTicketId, setUpdatingTicketId] = useState(null);

  const loadTab = useCallback(async (t) => {
    setLoading(true);
    try {
      if (t === 'orders') setOrders(await orderService.getAllOrders('Pending'));
      if (t === 'warehouse') setOrders(await orderService.getAllOrders('Confirmed'));
      if (t === 'delivery') setOrders(await orderService.getAllOrders('Shipping'));
      if (t === 'support') setTickets(await staffService.getSupportTickets({
        search: ticketFilters.search || undefined,
        status: ticketFilters.status || undefined,
      }));
    } catch (error) { setMsg(getApiErrorMessage(error, 'Không thể tải dữ liệu khu vực làm việc.')); }
    finally { setLoading(false); }
  }, [ticketFilters]);

  useEffect(() => { const run = async () => { await loadTab(tab); }; run(); }, [tab, loadTab]);

  useEffect(() => {
    if (!['orders', 'warehouse', 'delivery'].includes(tab)) return undefined;
    let active = true;
    let connection;
    const connect = async () => {
      try {
        const connected = await orderService.connectToNewOrderNotifications(async () => {
          if (!active || tab !== 'orders') return;
          setMsg('Có đơn hàng mới cần xử lý.');
          try {
            setOrders(await orderService.getAllOrders('Pending'));
          } catch {
            setMsg('Có đơn hàng mới, nhưng không tải lại được danh sách.');
          }
        }, async () => {
          if (!active) return;
          setMsg('Đơn hàng đã được xác nhận và chuyển vào hàng đợi xuất kho.');
          try {
            const status = tab === 'warehouse' ? 'Confirmed' : tab === 'delivery' ? 'Shipping' : 'Pending';
            setOrders(await orderService.getAllOrders(status));
          } catch {
            setMsg('Đơn đã xác nhận nhưng không tải lại được danh sách.');
          }
        }, async () => {
          if (!active) return;
          try {
            const status = tab === 'warehouse' ? 'Confirmed' : tab === 'delivery' ? 'Shipping' : 'Pending';
            setOrders(await orderService.getAllOrders(status));
          } catch {
            setMsg('Đơn hàng đã đổi trạng thái nhưng không tải lại được danh sách.');
          }
        });
        if (!active) {
          await connected.stop();
          return;
        }
        connection = connected;
      } catch {
        if (active) setMsg('Không kết nối được thông báo đơn hàng realtime. Hãy làm mới danh sách để kiểm tra đơn mới.');
      }
    };
    connect();
    return () => {
      active = false;
      connection?.stop();
    };
  }, [tab]);

  const handleAcceptOrder = async (id) => {
    setConfirmingOrderId(id);
    let confirmed = false;
    try {
      const result = await orderService.confirmOrder(id, staffName);
      confirmed = true;
      setOrders(current => current.filter(order => order.id !== id));
      setMsg(result.emailWarning
        ? `Đã xác nhận đơn hàng. ${result.emailWarning}`
        : `Đã xác nhận đơn hàng; tồn kho đã được kiểm tra và đặt trước.${result.emailQueued ? ' Email thông báo đã được xếp hàng gửi.' : ''}`);
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể xác nhận đơn hàng.'));
    } finally {
      setConfirmingOrderId(null);
    }
    if (confirmed) {
      try {
        setOrders(await orderService.getAllOrders('Pending'));
      } catch (error) {
        setMsg(`Đã xác nhận đơn hàng, nhưng không tải lại được danh sách: ${getApiErrorMessage(error, 'lỗi tải dữ liệu')}`);
      }
    }
  };

  const setDraftValue = (orderId, field, value) => {
    setStatusDrafts(current => ({
      ...current,
      [orderId]: { ...current[orderId], [field]: value }
    }));
  };

  const handleUpdateOrderStatus = async (order, status) => {
    const draft = statusDrafts[order.id] || {};
    setUpdatingOrderId(order.id);
    let updated = false;
    try {
      const result = await orderService.updateStatus(order.id, status, {
        trackingNumber: draft.trackingNumber,
        carrier: draft.carrier,
        note: draft.note,
        changedByName: staffName
      });
      updated = true;
      setOrders(current => current.filter(item => item.id !== order.id));
      setMsg(result.emailWarning
        ? `Đã cập nhật trạng thái. ${result.emailWarning}`
        : `Đã cập nhật trạng thái đơn ${order.orderCode}.${result.emailQueued ? ' Email thông báo đã được xếp hàng gửi.' : ''}`);
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật trạng thái. Vui lòng tải lại đơn và thử lại.'));
    } finally {
      setUpdatingOrderId(null);
    }
    if (updated) {
      try {
        const queryStatus = tab === 'warehouse' ? 'Confirmed' : tab === 'delivery' ? 'Shipping' : 'Pending';
        setOrders(await orderService.getAllOrders(queryStatus));
      } catch (error) {
        setMsg(`Đã cập nhật trạng thái đơn ${order.orderCode}, nhưng không tải lại được danh sách: ${getApiErrorMessage(error, 'lỗi tải dữ liệu')}`);
      }
    }
  };

  const handlePrint = (orderId, documentType) => {
    setDocumentOrder({ orderId, documentType });
  };

  const handleUpdateTicket = async (ticket, status) => {
    setUpdatingTicketId(ticket.id);
    try {
      await staffService.updateSupportTicket(ticket.id, status);
      setTickets(await staffService.getSupportTickets({
        search: ticketFilters.search || undefined,
        status: ticketFilters.status || undefined,
      }));
      setMsg(`Đã cập nhật trạng thái yêu cầu hỗ trợ #${ticket.id}.`);
    } catch (error) {
      setMsg(getApiErrorMessage(error, 'Không thể cập nhật yêu cầu hỗ trợ.'));
    } finally {
      setUpdatingTicketId(null);
    }
  };

  const visibleTabs = TABS.filter(tabItem => {
    if (tabItem.id === 'customers') return canLookupCustomers;
    if (tabItem.id === 'support') return true;
    const module = tabItem.id === 'compatibility' ? 'pcbuilder' : 'orders';
    const action = tabItem.id === 'compatibility' ? 'create' : 'view';
    return permissions.some(permission => permission.module === module
      && permission.action === action
      && permission.isAllowed);
  });
  const filteredOrders = orders.filter(order => {
    const query = orderSearch.trim().toLowerCase();
    return !query
      || order.orderCode?.toLowerCase().includes(query)
      || order.receiverName?.toLowerCase().includes(query)
      || order.receiverPhone?.toLowerCase().includes(query)
      || order.customerEmail?.toLowerCase().includes(query);
  });
  const orderValue = orders.reduce((total, order) => total + (Number(order.totalAmount) || 0), 0);
  const activeTab = TABS.find(item => item.id === tab) || TABS[0];
  const reloadCurrentTab = () => loadTab(tab);
  const handleLogout = () => {
    if (window.confirm('Bạn có chắc chắn muốn đăng xuất?')) onLogout?.();
  };

  return (
    <div className="min-h-screen w-full bg-slate-50">
      <div className="flex min-h-screen w-full flex-col overflow-hidden bg-slate-50">
        <header className="relative flex shrink-0 items-center justify-between overflow-hidden bg-slate-950 px-4 py-4 text-white shadow-[0_20px_60px_rgba(15,23,42,0.18)] sm:px-6 lg:px-8">
          <div className="pointer-events-none absolute inset-0 bg-[linear-gradient(135deg,rgba(59,130,246,0.28),transparent_45%,rgba(34,211,238,0.18))]" />
          <div className="relative flex min-w-0 items-center gap-3">
            <span className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-gradient-to-br from-blue-500 via-cyan-400 to-sky-300 shadow-lg shadow-blue-500/30">
              <BriefcaseBusiness className="h-5 w-5" />
            </span>
            <div className="min-w-0">
              <p className="text-[10px] font-black uppercase tracking-[0.24em] text-cyan-300">PCSTORE · WORKSPACE</p>
              <h2 className="truncate text-base font-black text-white sm:text-xl">Trung tâm làm việc</h2>
              <p className="mt-0.5 truncate text-[11px] text-slate-300">Xin chào, {staffName}. Chọn công việc để bắt đầu.</p>
            </div>
          </div>
          <div className="relative flex items-center gap-2 sm:gap-3">
            <span className="hidden items-center gap-1.5 rounded-full border border-emerald-400/25 bg-emerald-500/10 px-3 py-1.5 text-[10px] font-bold text-emerald-300 sm:inline-flex">
              <span className="h-1.5 w-1.5 rounded-full bg-emerald-400" /> Đang làm việc
            </span>
            <button type="button" onClick={onClose} aria-label="Quay lại cửa hàng" className="inline-flex items-center gap-2 rounded-xl border border-white/10 bg-white/5 px-3 py-2.5 text-[11px] font-bold text-slate-200 transition hover:bg-white/10 hover:text-white">
              <ArrowLeft className="h-4 w-4" /><span className="hidden sm:inline">Về cửa hàng</span>
            </button>
          </div>
        </header>

        <div className="grid min-h-0 flex-1 md:grid-cols-[240px_minmax(0,1fr)] lg:grid-cols-[260px_minmax(0,1fr)]">
          <aside className="flex shrink-0 flex-col border-b border-slate-200 bg-white/80 backdrop-blur md:border-b-0 md:border-r md:border-slate-200">
            <div className="hidden px-4 pb-2 pt-5 text-[10px] font-black uppercase tracking-[0.18em] text-slate-400 md:block">Không gian của bạn</div>
            <nav aria-label="Điều hướng công việc" className="flex gap-1 overflow-x-auto p-2 md:flex-1 md:flex-col md:overflow-y-auto md:px-3 md:py-3">
              {visibleTabs.map(tabItem => {
                const Icon = tabItem.icon;
                const isActive = tab === tabItem.id;
                return (
                  <button
                    key={tabItem.id}
                    type="button"
                    onClick={() => { setTab(tabItem.id); setMsg(''); setOrderSearch(''); }}
                    aria-current={isActive ? 'page' : undefined}
                    className={`group flex shrink-0 items-center gap-3 rounded-2xl px-3 py-2.5 text-left text-xs font-bold transition md:w-full ${
                      isActive
                        ? 'bg-gradient-to-r from-blue-600 to-cyan-500 text-white shadow-[0_12px_30px_rgba(37,99,235,0.28)]'
                        : 'text-slate-600 hover:bg-slate-100 hover:text-slate-900'
                    }`}
                  >
                    <Icon className={`h-4 w-4 shrink-0 ${isActive ? 'text-white' : 'text-slate-400 group-hover:text-blue-600'}`} />
                    <span className="whitespace-nowrap">{tabItem.label}</span>
                    {isActive && <ArrowUpRight className="ml-auto hidden h-3.5 w-3.5 md:block" />}
                  </button>
                );
              })}
            </nav>
            {onManageCatalog && (
              <div className="border-t border-slate-100 p-2 md:p-3">
                <button type="button" onClick={onManageCatalog} className="flex w-full items-center gap-2 rounded-2xl px-3 py-2.5 text-left text-[11px] font-bold text-slate-600 transition hover:bg-blue-50 hover:text-blue-700">
                  <FolderTree className="h-4 w-4" /> Sản phẩm & danh mục
                </button>
              </div>
            )}
            <div className="grid grid-cols-3 gap-1 border-t border-slate-100 p-2 md:grid-cols-1 md:gap-1.5 md:p-3">
              <button type="button" onClick={() => setProfileSection('info')} className="flex items-center justify-center gap-2 rounded-2xl px-2 py-2.5 text-[10px] font-bold text-slate-600 transition hover:bg-blue-50 hover:text-blue-700 md:justify-start md:px-3 md:text-[11px]">
                <UserRound className="h-4 w-4 shrink-0" /><span>Thông tin cá nhân</span>
              </button>
              <button type="button" onClick={() => setProfileSection('password')} className="flex items-center justify-center gap-2 rounded-2xl px-2 py-2.5 text-[10px] font-bold text-slate-600 transition hover:bg-blue-50 hover:text-blue-700 md:justify-start md:px-3 md:text-[11px]">
                <KeyRound className="h-4 w-4 shrink-0" /><span>Đổi mật khẩu</span>
              </button>
              <button type="button" onClick={handleLogout} className="flex items-center justify-center gap-2 rounded-2xl px-2 py-2.5 text-[10px] font-bold text-red-600 transition hover:bg-red-50 md:justify-start md:px-3 md:text-[11px]">
                <LogOut className="h-4 w-4 shrink-0" /><span>Đăng xuất</span>
              </button>
            </div>
          </aside>

          <main className="min-h-0 overflow-y-auto bg-transparent p-3 sm:p-5 lg:p-7">
            <div className="mb-5 flex flex-wrap items-end justify-between gap-3 rounded-3xl border border-slate-200/70 bg-white/80 p-4 shadow-[0_12px_30px_rgba(148,163,184,0.12)] backdrop-blur-sm">
              <div>
                <p className="text-[10px] font-black uppercase tracking-[0.18em] text-blue-600">Bảng điều khiển</p>
                <h1 className="mt-1 text-xl font-black tracking-tight text-slate-900 sm:text-2xl">{activeTab.label}</h1>
                <p className="mt-1 text-xs text-slate-500">Theo dõi và xử lý công việc tại một nơi.</p>
              </div>
              <button type="button" onClick={reloadCurrentTab} disabled={loading} className="inline-flex items-center gap-2 rounded-xl border border-slate-200 bg-slate-50 px-3 py-2 text-xs font-bold text-slate-600 shadow-sm transition hover:border-blue-200 hover:bg-blue-50 hover:text-blue-700 disabled:opacity-50">
                <RefreshCw className={`h-3.5 w-3.5 ${loading ? 'animate-spin' : ''}`} /> Làm mới
              </button>
            </div>

            {['orders', 'warehouse', 'delivery'].includes(tab) && (
              <div className="mb-5 grid gap-3 md:grid-cols-2 xl:grid-cols-3">
                <div className="rounded-3xl border border-blue-100 bg-gradient-to-br from-blue-50 to-white p-4 shadow-[0_10px_24px_rgba(59,130,246,0.08)]">
                  <div className="flex items-center justify-between">
                    <span className="text-[10px] font-black uppercase tracking-[0.16em] text-slate-500">Đang trong danh sách</span>
                    <span className="rounded-xl bg-blue-600 p-2.5 text-white shadow-sm"><Package className="h-4 w-4" /></span>
                  </div>
                  <p className="mt-3 text-3xl font-black tracking-tight text-slate-900">{orders.length}</p>
                  <p className="mt-1 text-[10px] text-slate-500">{tab === 'orders' ? 'đơn chờ xác nhận' : tab === 'warehouse' ? 'đơn chờ xuất kho' : 'đơn đang giao'}</p>
                </div>
                <div className="rounded-3xl border border-emerald-100 bg-gradient-to-br from-emerald-50 to-white p-4 shadow-[0_10px_24px_rgba(16,185,129,0.08)]">
                  <div className="flex items-center justify-between">
                    <span className="text-[10px] font-black uppercase tracking-[0.16em] text-slate-500">Tổng giá trị</span>
                    <span className="rounded-xl bg-emerald-500 p-2.5 text-white shadow-sm"><Wallet className="h-4 w-4" /></span>
                  </div>
                  <p className="mt-3 text-2xl font-black tracking-tight text-slate-900">{orderValue.toLocaleString('vi-VN')}<span className="ml-1 text-xs font-bold text-slate-400">đ</span></p>
                  <p className="mt-1 text-[10px] text-slate-500">trên danh sách đang xem</p>
                </div>
                <div className="rounded-3xl border border-violet-100 bg-gradient-to-br from-violet-50 to-white p-4 shadow-[0_10px_24px_rgba(139,92,246,0.08)] md:col-span-2 xl:col-span-1">
                  <div className="flex items-center justify-between">
                    <span className="text-[10px] font-black uppercase tracking-[0.16em] text-slate-500">Trạng thái</span>
                    <span className="rounded-xl bg-violet-500 p-2.5 text-white shadow-sm"><CircleCheck className="h-4 w-4" /></span>
                  </div>
                  <p className="mt-3 text-sm font-black text-slate-900">{orders.length ? 'Sẵn sàng xử lý' : 'Đã cập nhật'}</p>
                  <p className="mt-1 text-[10px] text-slate-500">Đồng bộ danh sách theo thời gian thực</p>
                </div>
              </div>
            )}

            {msg && (
              <div role="status" className="mb-4 flex items-start justify-between gap-3 rounded-2xl border border-blue-100 bg-blue-50 px-4 py-3 text-xs font-medium text-blue-800 shadow-sm">
                <span>{msg}</span>
                <button type="button" onClick={() => setMsg('')} aria-label="Đóng thông báo" className="shrink-0 text-blue-500 hover:text-blue-800"><X className="h-4 w-4" /></button>
              </div>
            )}
            {['orders', 'warehouse', 'delivery'].includes(tab) && (
              <label className="mb-4 flex items-center gap-2 rounded-2xl border border-slate-200 bg-white px-3.5 py-2.5 shadow-[0_8px_20px_rgba(148,163,184,0.08)] ring-0 transition focus-within:border-blue-300 focus-within:ring-2 focus-within:ring-blue-100">
                <Search className="h-4 w-4 shrink-0 text-slate-400" />
                <input value={orderSearch} onChange={event => setOrderSearch(event.target.value)} placeholder="Tìm mã đơn, tên, SĐT hoặc email..." className="w-full bg-transparent text-xs text-slate-800 outline-none placeholder:text-slate-400" />
                {orderSearch && <button type="button" onClick={() => setOrderSearch('')} className="text-[10px] font-bold text-blue-600">Xóa</button>}
              </label>
            )}
          {loading ? (
            <div className="rounded-3xl border border-slate-200 bg-white/90 py-16 text-center shadow-[0_12px_30px_rgba(148,163,184,0.08)]"><Loader2 className="mx-auto h-7 w-7 animate-spin text-blue-600" /><p className="mt-3 text-xs font-semibold text-slate-500">Đang tải dữ liệu...</p></div>
          ) : (
            <>
              {['orders', 'warehouse', 'delivery'].includes(tab) && (
                <div className="space-y-3">
                  {orders.length === 0 ? <div className="rounded-3xl border border-dashed border-slate-300 bg-white/80 px-4 py-14 text-center shadow-[0_12px_28px_rgba(148,163,184,0.06)]">
                    <span className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-slate-100 text-slate-400"><Package className="h-5 w-5" /></span>
                    <p className="mt-3 text-sm font-bold text-slate-700">{
                    tab === 'warehouse' ? 'Chưa có phiếu xuất kho chờ xử lý.'
                      : tab === 'delivery' ? 'Không có đơn đang vận chuyển.'
                        : 'Không có đơn chờ xử lý.'
                  }</p><p className="mt-1 text-xs text-slate-400">Danh sách sẽ tự cập nhật khi có thay đổi.</p></div>
                    : filteredOrders.length === 0 ? <div className="rounded-3xl border border-dashed border-slate-300 bg-white/80 px-4 py-12 text-center text-xs text-slate-500 shadow-[0_12px_28px_rgba(148,163,184,0.06)]">Không tìm thấy đơn hàng phù hợp với “{orderSearch}”.</div>
                      : filteredOrders.map(o => (
                    <div key={o.id} className="overflow-hidden rounded-3xl border border-slate-200 bg-white shadow-[0_12px_30px_rgba(15,23,42,0.04)] transition hover:border-blue-200 hover:shadow-[0_18px_36px_rgba(59,130,246,0.08)] sm:p-0">
                      <div className="flex flex-wrap items-start justify-between gap-3 border-b border-slate-100 bg-gradient-to-r from-slate-50 via-white to-blue-50 px-4 py-3 sm:px-5">
                        <div className="flex min-w-0 items-center gap-3">
                          <span className="flex h-10 w-10 items-center justify-center rounded-2xl bg-blue-100 text-blue-700"><Package className="h-4 w-4" /></span>
                          <div className="min-w-0">
                            <p className="text-[10px] font-black uppercase tracking-[0.18em] text-slate-400">Mã đơn</p>
                            <span className="block text-base font-black text-blue-700">{o.orderCode}</span>
                          </div>
                        </div>
                        <span className="rounded-full border border-amber-200 bg-amber-50 px-3 py-1 text-[10px] font-bold text-amber-700">{ORDER_STATUS_LABELS[o.status] || o.status}</span>
                      </div>

                      <div className="grid gap-3 p-4 sm:grid-cols-2 xl:grid-cols-4 sm:p-5">
                        <div className="rounded-2xl bg-slate-50 p-3">
                          <p className="text-[10px] font-black uppercase tracking-[0.14em] text-slate-400">Người nhận</p>
                          <p className="mt-2 text-sm font-bold text-slate-800">{o.receiverName}</p>
                          <p className="text-[11px] text-slate-500">{o.receiverPhone}</p>
                        </div>
                        <div className="rounded-2xl bg-slate-50 p-3">
                          <p className="text-[10px] font-black uppercase tracking-[0.14em] text-slate-400">Giá trị</p>
                          <p className="mt-2 text-sm font-black text-slate-900">{o.totalAmount?.toLocaleString('vi-VN')}đ</p>
                          <p className="text-[11px] text-slate-500">{o.itemCount} sản phẩm</p>
                        </div>
                        <div className="rounded-2xl bg-slate-50 p-3">
                          <p className="text-[10px] font-black uppercase tracking-[0.14em] text-slate-400">Email</p>
                          <p className="mt-2 line-clamp-2 text-sm font-medium text-slate-700">{o.customerEmail || '—'}</p>
                        </div>
                        <div className="rounded-2xl bg-slate-50 p-3">
                          <p className="text-[10px] font-black uppercase tracking-[0.14em] text-slate-400">Cập nhật</p>
                          <p className="mt-2 text-sm font-bold text-slate-800">{o.updatedAt ? new Date(o.updatedAt).toLocaleString('vi-VN', { dateStyle: 'short', timeStyle: 'short' }) : '—'}</p>
                        </div>
                      </div>

                      {tab === 'orders' && <div className="flex flex-wrap gap-2 border-t border-slate-100 bg-slate-50/70 px-4 py-3 sm:px-5">
                        <button
                          onClick={() => handleAcceptOrder(o.id)}
                          disabled={confirmingOrderId === o.id}
                          className="inline-flex items-center gap-1.5 rounded-xl bg-emerald-600 px-3 py-2 text-[11px] font-bold text-white transition hover:bg-emerald-700 disabled:opacity-50"
                        >
                          <CheckCircle2 className="w-3 h-3" />
                          {confirmingOrderId === o.id ? 'Đang kiểm tra kho...' : 'Xác nhận đơn hàng'}
                        </button>
                        <button
                          onClick={() => handleUpdateOrderStatus(o, 'Cancelled')}
                          disabled={updatingOrderId === o.id}
                          className="rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-[11px] font-bold text-rose-600 transition hover:bg-rose-100 disabled:opacity-50"
                        >
                          Hủy đơn
                        </button>
                        <button
                          onClick={() => setExpandedOrderId(current => current === o.id ? null : o.id)}
                          className="flex items-center gap-1.5 rounded-xl border border-slate-200 bg-white px-3 py-2 text-[11px] font-bold text-slate-600 transition hover:bg-slate-50"
                        >
                          <Eye className="w-3 h-3" />{expandedOrderId === o.id ? 'Ẩn chi tiết' : 'Xem chi tiết'}
                        </button>
                      </div>}
                      {(o.status === 'Confirmed' || o.status === 'Shipping') && (
                        <div className="mt-2 flex flex-wrap gap-1 px-4 pb-3 sm:px-5">
                          <button
                            onClick={() => handlePrint(o.id, 'packing-slip')}
                            className="flex items-center gap-1.5 rounded-lg border border-indigo-200 bg-indigo-50 px-3 py-2 text-[11px] font-bold text-indigo-700 hover:bg-indigo-100"
                          >
                            <Printer className="w-3 h-3" /> In phiếu đóng gói
                          </button>
                          <button
                            onClick={() => handlePrint(o.id, 'invoice')}
                            className="flex items-center gap-1.5 rounded-lg border border-indigo-200 bg-indigo-50 px-3 py-2 text-[11px] font-bold text-indigo-700 hover:bg-indigo-100"
                          >
                            <Printer className="w-3 h-3" /> In hóa đơn
                          </button>
                        </div>
                      )}
                      {expandedOrderId === o.id && (
                        <div className="mt-3 border-t border-slate-100 px-4 py-4 text-[11px] sm:px-5">
                          <p className="text-slate-500">Địa chỉ giao: <span className="text-slate-800">{o.shippingAddress}</span></p>
                          <p className="mt-1 text-slate-500">Email: <span className="text-slate-800">{o.customerEmail || 'Không có'}</span></p>
                          <div className="mt-2 divide-y divide-slate-100">
                            {o.items?.map(item => (
                              <div key={item.productId} className="flex justify-between gap-3 py-2">
                                <span className="text-slate-700">{item.productName} <span className="text-slate-400">({item.sku})</span> × {item.quantity}</span>
                                <span className="shrink-0 text-slate-600">{(item.unitPrice * item.quantity).toLocaleString('vi-VN')}đ</span>
                              </div>
                            ))}
                          </div>
                          {o.statusHistory?.length > 0 && (
                            <ol className="mt-3 space-y-2 border-t border-slate-100 pt-3">
                              {o.statusHistory.map((history, index) => (
                                <li key={`${history.status}-${history.changedAt}-${index}`} className="text-[10px] text-slate-500">
                                  <span className="font-bold text-slate-700">{ORDER_STATUS_LABELS[history.status] || history.status}</span>
                                  {' · '}{new Date(history.changedAt).toLocaleString('vi-VN')}
                                  {history.changedByName && ` · ${history.changedByName}`}
                                  {history.carrier && ` · ${history.carrier}`}
                                  {history.trackingNumber && ` · ${history.trackingNumber}`}
                                  {history.note && <p className="mt-0.5 text-slate-500">Ghi chú: {history.note}</p>}
                                </li>
                              ))}
                            </ol>
                          )}
                        </div>
                      )}
                      {tab === 'warehouse' && (
                        <div className="border-t border-slate-100 bg-slate-50/70 px-4 py-4 sm:px-5">
                          <p className="mb-2 text-[10px] font-bold uppercase text-emerald-700">Đơn đã xác nhận · Sẵn sàng soạn hàng</p>
                          <div className="divide-y divide-slate-100">
                            {o.items?.map(item => (
                              <div key={item.productId} className="flex justify-between gap-3 py-2 text-[11px]">
                                <span className="text-slate-700">{item.productName} <span className="text-slate-400">({item.sku})</span></span>
                                <span className="shrink-0 text-slate-600">SL {item.quantity}</span>
                              </div>
                            ))}
                          </div>
                          <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2">
                            <select
                              value={statusDrafts[o.id]?.carrier || ''}
                              onChange={event => setDraftValue(o.id, 'carrier', event.target.value)}
                              aria-label={`Đơn vị vận chuyển đơn ${o.orderCode}`}
                              className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[11px] text-slate-800 outline-none focus:border-blue-400"
                            >
                              <option value="">Chọn đơn vị vận chuyển *</option>
                              {CARRIERS.map(carrier => <option key={carrier} value={carrier}>{carrier}</option>)}
                            </select>
                            <input
                              value={statusDrafts[o.id]?.trackingNumber || ''}
                              onChange={event => setDraftValue(o.id, 'trackingNumber', event.target.value)}
                              maxLength={100}
                              placeholder="Mã vận đơn *"
                              aria-label={`Mã vận đơn đơn ${o.orderCode}`}
                              className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[11px] text-slate-800 outline-none focus:border-blue-400"
                            />
                            <input
                              value={statusDrafts[o.id]?.note || ''}
                              onChange={event => setDraftValue(o.id, 'note', event.target.value)}
                              maxLength={500}
                              placeholder="Ghi chú (không bắt buộc)"
                              className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-[11px] text-slate-800 outline-none focus:border-blue-400 sm:col-span-2"
                            />
                            <div className="flex gap-2 sm:col-span-2">
                              <button
                                onClick={() => handleUpdateOrderStatus(o, 'Shipping')}
                                disabled={updatingOrderId === o.id || !statusDrafts[o.id]?.carrier || !statusDrafts[o.id]?.trackingNumber?.trim()}
                                className="rounded-xl bg-blue-600 px-3 py-2 text-[11px] font-bold text-white transition hover:bg-blue-700 disabled:opacity-50"
                              >
                                {updatingOrderId === o.id ? 'Đang cập nhật...' : 'Bàn giao vận chuyển'}
                              </button>
                              <button
                                onClick={() => handleUpdateOrderStatus(o, 'Cancelled')}
                                disabled={updatingOrderId === o.id}
                                className="rounded-xl bg-rose-600 px-3 py-2 text-[11px] font-bold text-white transition hover:bg-rose-700 disabled:opacity-50"
                              >
                                Hủy đơn
                              </button>
                            </div>
                          </div>
                        </div>
                      )}
                      {tab === 'delivery' && (
                        <div className="border-t border-slate-100 bg-slate-50/70 px-4 py-4 sm:px-5">
                          <p className="text-[11px] font-bold text-blue-700">
                            {[...(o.statusHistory || [])].reverse().find(entry => entry.status === 'Shipping')?.carrier || 'Đơn vị vận chuyển'}
                            {' · Mã vận đơn: '}
                            {[...(o.statusHistory || [])].reverse().find(entry => entry.status === 'Shipping')?.trackingNumber || '—'}
                          </p>
                          <input
                            value={statusDrafts[o.id]?.note || ''}
                            onChange={event => setDraftValue(o.id, 'note', event.target.value)}
                            maxLength={500}
                            placeholder="Ghi chú cập nhật (không bắt buộc)"
                            className="mt-2 w-full rounded-xl border border-slate-200 bg-white px-3 py-2 text-[11px] text-slate-800 outline-none focus:border-blue-400"
                          />
                          <div className="mt-2 flex flex-wrap gap-2">
                            <button
                              onClick={() => handleUpdateOrderStatus(o, 'Completed')}
                              disabled={updatingOrderId === o.id}
                              className="rounded-xl bg-emerald-600 px-3 py-2 text-[11px] font-bold text-white transition hover:bg-emerald-700 disabled:opacity-50"
                            >
                              Xác nhận giao thành công
                            </button>
                            <button
                              onClick={() => handleUpdateOrderStatus(o, 'DeliveryFailed')}
                              disabled={updatingOrderId === o.id}
                              className="rounded-xl bg-rose-600 px-3 py-2 text-[11px] font-bold text-white transition hover:bg-rose-700 disabled:opacity-50"
                            >
                              Giao thất bại · Hoàn kho
                            </button>
                          </div>
                        </div>
                      )}
                    </div>
                  ))}
                </div>
              )}

              {tab === 'customers' && (
                canLookupCustomers ? <CustomerLookup /> : <p className="text-center text-slate-500">Bạn không có quyền tra cứu hồ sơ khách hàng.</p>
              )}

              {tab === 'compatibility' && <StaffCompatibilityTool />}

              {tab === 'support' && (
                <div className="space-y-3">
                  <form
                    onSubmit={event => {
                      event.preventDefault();
                      setTicketFilters({
                        search: ticketSearch.trim(),
                        status: ticketStatusFilter,
                      });
                    }}
                    className="flex flex-wrap gap-2"
                  >
                    <input
                      value={ticketSearch}
                      onChange={event => setTicketSearch(event.target.value)}
                      maxLength={200}
                      placeholder="Tìm tên, email, tiêu đề hoặc nội dung"
                      className="min-w-[220px] flex-1 rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs text-slate-800"
                    />
                    <select
                      value={ticketStatusFilter}
                      onChange={event => setTicketStatusFilter(event.target.value)}
                      className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-xs text-slate-800"
                    >
                      <option value="">Tất cả trạng thái</option>
                      <option value="Open">Mới</option>
                      <option value="InProgress">Đang xử lý</option>
                      <option value="Resolved">Đã giải quyết</option>
                      <option value="Closed">Đã đóng</option>
                    </select>
                    <button type="submit" className="rounded-xl bg-blue-600 px-4 py-2 text-xs font-bold text-white">
                      Tìm yêu cầu
                    </button>
                  </form>
                  {tickets.length === 0 && <p className="rounded-xl bg-white p-4 text-xs text-slate-500">Không có yêu cầu hỗ trợ phù hợp.</p>}
                  {tickets.map(t => (
                    <div key={t.id} className="flex flex-wrap items-start justify-between gap-3 rounded-3xl border border-slate-200 bg-white p-4 shadow-[0_10px_24px_rgba(148,163,184,0.08)]">
                      <div className="min-w-[200px] flex-1">
                        <p className="text-sm font-bold text-slate-800">{t.subject}</p>
                        <p className="mt-1 text-xs text-slate-500">{t.customer} · {t.customerEmail}</p>
                        <p className="mt-2 whitespace-pre-wrap text-xs text-slate-700">{t.description}</p>
                        <p className="mt-2 text-[10px] text-slate-400">Tạo lúc {new Date(t.createdAt).toLocaleString('vi-VN')}</p>
                      </div>
                      <div className="flex items-center gap-2">
                        <span className={`rounded-full px-3 py-1 text-[10px] font-bold ${t.status === 'Open' ? 'bg-amber-50 text-amber-700' : t.status === 'Resolved' || t.status === 'Closed' ? 'bg-emerald-50 text-emerald-700' : 'bg-blue-50 text-blue-700'}`}>{t.status}</span>
                        <select
                          value={t.status}
                          onChange={event => handleUpdateTicket(t, event.target.value)}
                          disabled={updatingTicketId === t.id}
                          aria-label={`Trạng thái ticket ${t.id}`}
                          className="rounded-lg border border-slate-200 bg-white px-2 py-1.5 text-xs text-slate-700 disabled:opacity-50"
                        >
                          <option value="Open">Mới</option>
                          <option value="InProgress">Đang xử lý</option>
                          <option value="Resolved">Đã giải quyết</option>
                          <option value="Closed">Đã đóng</option>
                        </select>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </>
          )}
          </main>
        </div>
      </div>

      {documentOrder && (
        <OrderDocumentPreview
          orderId={documentOrder.orderId}
          documentType={documentOrder.documentType}
          staffName={staffName}
          onClose={() => setDocumentOrder(null)}
        />
      )}
      <ProfileModal
        key={profileSection || 'closed'}
        isOpen={profileSection !== null}
        initialTab={profileSection || 'info'}
        currentUser={currentUser}
        onUserUpdated={onUserUpdated}
        onClose={() => setProfileSection(null)}
      />
    </div>
  );
}
