import React, { useState, useEffect, useCallback } from 'react';
import { 
  Cpu, ShoppingCart, Wrench, Search, ShieldCheck, Truck, 
  RotateCcw, Sparkles, ChevronRight, Layers, 
  Monitor, Zap, Trash2, Plus, AlertCircle, CheckCircle2, Bot, Send, X,
  Headphones, HardDrive, Fan, Box, UserPlus, LogIn, LogOut, Loader2, Mail, Lock, User, Phone, MapPin, AlertTriangle, RefreshCw, Settings, KeyRound, Filter, SlidersHorizontal, ArrowUpDown, Eye
} from 'lucide-react';
import { productService, authService } from './services/api';
import ProductDetailModal from './components/ProductDetailModal';

export default function App() {
  const [activeTab, setActiveTab] = useState('home');
  const [categories, setCategories] = useState([]);
  const [products, setProducts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [apiError, setApiError] = useState(null);

  // States Tìm kiếm & Lọc
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedCategoryTab, setSelectedCategoryTab] = useState('ALL');
  const [priceRange, setPriceRange] = useState('ALL'); // 'ALL' | 'UNDER_5M' | '5M_15M' | 'OVER_15M'
  const [sortBy, setSortBy] = useState('default'); // 'default' | 'price_asc' | 'price_desc'
  
  // State Xem chi tiết sản phẩm
  const [selectedProductId, setSelectedProductId] = useState(null);
  const [isDetailOpen, setIsDetailOpen] = useState(false);

  // Auth state & Modals
  const [isRegisterOpen, setIsRegisterOpen] = useState(false);
  const [isLoginOpen, setIsLoginOpen] = useState(false);
  const [isProfileOpen, setIsProfileOpen] = useState(false);
  const [currentUser, setCurrentUser] = useState(() => {
    try {
      const saved = localStorage.getItem('pcstore_user');
      return saved ? JSON.parse(saved) : null;
    } catch {
      return null;
    }
  });

  // Login form state
  const [loginForm, setLoginForm] = useState({ email: '', password: '' });
  const [loginLoading, setLoginLoading] = useState(false);
  const [loginError, setLoginError] = useState('');
  const [loginSuccess, setLoginSuccess] = useState('');

  // Register form state
  const [registerForm, setRegisterForm] = useState({ fullName: '', email: '', password: '', confirmPassword: '', phoneNumber: '', address: '' });
  const [registerLoading, setRegisterLoading] = useState(false);
  const [registerError, setRegisterError] = useState('');
  const [registerSuccess, setRegisterSuccess] = useState('');

  // Profile form state
  const [profileForm, setProfileForm] = useState({ fullName: '', phoneNumber: '', address: '', currentPassword: '', newPassword: '' });
  const [profileLoading, setProfileLoading] = useState(false);
  const [profileError, setProfileError] = useState('');
  const [profileSuccess, setProfileSuccess] = useState('');

  // Builder state
  const [selectedParts, setSelectedParts] = useState({});
  const [modalCategory, setModalCategory] = useState(null);
  
  // AI Chat state
  const [isChatOpen, setIsChatOpen] = useState(false);
  const [messages, setMessages] = useState([
    { sender: 'bot', text: 'Xin chào! Tôi là Trợ lý AI PCSTORE. Bạn cần tư vấn cấu hình dàn máy nào?' }
  ]);
  const [chatInput, setChatInput] = useState('');

  // Hàm tải dữ liệu sản phẩm với bộ lọc
  const fetchProducts = useCallback(async () => {
    try {
      setLoading(true);
      setApiError(null);

      let minPrice = undefined;
      let maxPrice = undefined;

      if (priceRange === 'UNDER_5M') {
        maxPrice = 5000000;
      } else if (priceRange === '5M_15M') {
        minPrice = 5000000;
        maxPrice = 15000000;
      } else if (priceRange === 'OVER_15M') {
        minPrice = 15000000;
      }

      const prodData = await productService.getProducts({
        searchTerm: searchTerm.trim(),
        categoryType: selectedCategoryTab,
        minPrice,
        maxPrice,
        sortBy
      });

      setProducts(Array.isArray(prodData) ? prodData : []);
    } catch (err) {
      setApiError('Không thể kết nối đến Backend API PostgreSQL (http://localhost:5170).');
    } finally {
      setLoading(false);
    }
  }, [searchTerm, selectedCategoryTab, priceRange, sortBy]);

  // Nạp danh mục lần đầu
  useEffect(() => {
    const fetchInitCategories = async () => {
      try {
        const catData = await productService.getCategories();
        setCategories(Array.isArray(catData) ? catData : []);
      } catch (err) {
        console.error('Lỗi khi tải danh mục:', err);
      }
    };
    fetchInitCategories();
  }, []);

  // Tự động gọi API khi thay đổi bộ lọc
  useEffect(() => {
    fetchProducts();
  }, [fetchProducts]);

  // Mở modal hồ sơ
  const handleOpenProfile = async () => {
    if (!currentUser) return;
    setProfileError('');
    setProfileSuccess('');
    setProfileForm({
      fullName: currentUser.fullName || '',
      phoneNumber: currentUser.phoneNumber || '',
      address: currentUser.address || '',
      currentPassword: '',
      newPassword: ''
    });
    setIsProfileOpen(true);

    try {
      const data = await authService.getProfile(currentUser.id);
      setProfileForm(prev => ({
        ...prev,
        fullName: data.fullName || '',
        phoneNumber: data.phoneNumber || '',
        address: data.address || ''
      }));
    } catch (err) {
      console.warn('Lỗi lấy hồ sơ mới nhất:', err);
    }
  };

  // Cập nhật hồ sơ & đổi mật khẩu
  const handleProfileSubmit = async (e) => {
    e.preventDefault();
    setProfileError('');
    setProfileSuccess('');

    try {
      setProfileLoading(true);
      const res = await authService.updateProfile(currentUser.id, profileForm);
      setProfileSuccess(res.message || 'Cập nhật hồ sơ cá nhân thành công!');
      setCurrentUser(res.user);
      localStorage.setItem('pcstore_user', JSON.stringify(res.user));

      setTimeout(() => {
        setIsProfileOpen(false);
        setProfileSuccess('');
      }, 1000);
    } catch (err) {
      setProfileError(err.response?.data?.detail || err.response?.data?.message || 'Lỗi khi cập nhật hồ sơ.');
    } finally {
      setProfileLoading(false);
    }
  };

  // Đăng nhập
  const handleLoginSubmit = async (e) => {
    e.preventDefault();
    setLoginError('');
    setLoginSuccess('');

    try {
      setLoginLoading(true);
      const res = await authService.login(loginForm);
      setLoginSuccess('Đăng nhập thành công!');
      setCurrentUser(res.user);
      localStorage.setItem('pcstore_user', JSON.stringify(res.user));

      setTimeout(() => {
        setIsLoginOpen(false);
        setLoginSuccess('');
        setLoginForm({ email: '', password: '' });
      }, 800);
    } catch (err) {
      setLoginError(err.response?.data?.message || 'Email hoặc mật khẩu không chính xác!');
    } finally {
      setLoginLoading(false);
    }
  };

  // Đăng ký
  const handleRegisterSubmit = async (e) => {
    e.preventDefault();
    setRegisterError('');
    setRegisterSuccess('');

    if (registerForm.password !== registerForm.confirmPassword) {
      setRegisterError('Mật khẩu xác nhận không khớp!');
      return;
    }
    if (registerForm.password.length < 6) {
      setRegisterError('Mật khẩu phải từ 6 ký tự trở lên.');
      return;
    }

    try {
      setRegisterLoading(true);
      const res = await authService.register(registerForm);
      setRegisterSuccess('Đăng ký thành công!');
      setCurrentUser(res.user);
      localStorage.setItem('pcstore_user', JSON.stringify(res.user));

      setTimeout(() => {
        setIsRegisterOpen(false);
        setRegisterSuccess('');
        setRegisterForm({ fullName: '', email: '', password: '', confirmPassword: '', phoneNumber: '', address: '' });
      }, 1000);
    } catch (err) {
      setRegisterError(err.response?.data?.message || 'Lỗi khi đăng ký tài khoản.');
    } finally {
      setRegisterLoading(false);
    }
  };

  // Đăng xuất
  const handleLogout = async () => {
    try {
      await authService.logout();
    } catch (err) {
      console.warn('Lỗi API logout:', err);
    } finally {
      localStorage.removeItem('pcstore_user');
      setCurrentUser(null);
    }
  };

  // Reset bộ lọc
  const handleResetFilters = () => {
    setSearchTerm('');
    setSelectedCategoryTab('ALL');
    setPriceRange('ALL');
    setSortBy('default');
  };

  // Mở modal chi tiết
  const handleOpenDetail = (productId) => {
    setSelectedProductId(productId);
    setIsDetailOpen(true);
  };

  // Tính chi phí & TDP
  const totalPrice = Object.values(selectedParts).reduce((sum, item) => sum + (item?.price || 0), 0);
  const estimatedTdp = (selectedParts['CPU']?.tdpWattage || 0) + 
                       (selectedParts['GPU']?.tdpWattage || 0) + 
                       (selectedParts['RAM']?.tdpWattage || 10) + 50;
  const psuWattage = selectedParts['PSU']?.tdpWattage || selectedParts['PSU']?.recommendedPsu || 0;

  const compatibilityErrors = [];
  const cpu = selectedParts['CPU'];
  const mb = selectedParts['Mainboard'];
  const ram = selectedParts['RAM'];

  if (cpu && mb && cpu.socket && mb.socket && cpu.socket.trim().toLowerCase() !== mb.socket.trim().toLowerCase()) {
    compatibilityErrors.push(`CPU (${cpu.socket}) không tương thích chân cắm Socket với Mainboard (${mb.socket})!`);
  }
  if (mb && ram && mb.ramType && ram.ramType && !mb.ramType.toLowerCase().includes(ram.ramType.toLowerCase())) {
    compatibilityErrors.push(`RAM (${ram.ramType}) không khớp chuẩn hỗ trợ của Mainboard (${mb.ramType})!`);
  }
  if (psuWattage > 0 && estimatedTdp > psuWattage) {
    compatibilityErrors.push(`Nguồn (${psuWattage}W) không đủ tải công suất ước tính (${estimatedTdp}W)!`);
  }

  const handleSelectPart = (product) => {
    setSelectedParts(prev => ({ ...prev, [product.categoryType]: product }));
    setModalCategory(null);
  };

  const handleRemovePart = (categoryType) => {
    setSelectedParts(prev => {
      const updated = { ...prev };
      delete updated[categoryType];
      return updated;
    });
  };

  const handleSendMessage = (e) => {
    e.preventDefault();
    if (!chatInput.trim()) return;
    const userMsg = chatInput;
    setMessages(prev => [...prev, { sender: 'user', text: userMsg }]);
    setChatInput('');

    setTimeout(() => {
      setMessages(prev => [...prev, { 
        sender: 'bot', 
        text: `Với yêu cầu "${userMsg}", combo tối ưu là: CPU Intel Core i5 13400F + Main ASUS B760M + VGA RTX 4060 + Nguồn 650W Bronze.` 
      }]);
    }, 600);
  };

  const renderProductIcon = (type) => {
    switch (type) {
      case 'CPU': return <Cpu className="w-8 h-8 text-indigo-600" />;
      case 'Mainboard': return <Layers className="w-8 h-8 text-cyan-600" />;
      case 'RAM': return <HardDrive className="w-8 h-8 text-emerald-600" />;
      case 'GPU': return <Monitor className="w-8 h-8 text-green-600" />;
      case 'SSD': return <HardDrive className="w-8 h-8 text-purple-600" />;
      case 'PSU': return <Zap className="w-8 h-8 text-amber-500" />;
      case 'Case': return <Box className="w-8 h-8 text-slate-700" />;
      case 'Cooler': return <Fan className="w-8 h-8 text-sky-500" />;
      default: return <Cpu className="w-8 h-8 text-indigo-600" />;
    }
  };

  return (
    <div className="min-h-screen bg-slate-50 text-slate-800 flex flex-col font-sans selection:bg-indigo-500 selection:text-white">
      
      {/* 1. TOP HEADER */}
      <div className="bg-slate-900 text-slate-300 text-xs py-2 px-4 hidden md:block">
        <div className="max-w-7xl mx-auto flex justify-between items-center">
          <div className="flex gap-6 font-medium">
            <span>Showroom: <strong className="text-white">Đà Nẵng</strong> • Hotline: <strong className="text-cyan-400">1900 6868</strong></span>
            <span>Kỹ thuật: <strong className="text-emerald-400">0905 123 456</strong></span>
          </div>
          <div className="flex items-center gap-4 text-slate-400">
            <span className="hover:text-cyan-300 cursor-pointer">Bảo hành 36 tháng 1 đổi 1</span>
            <span className="hover:text-cyan-300 cursor-pointer">PostgreSQL Live Data</span>
          </div>
        </div>
      </div>

      {/* 2. NAVBAR */}
      <header className="sticky top-0 z-40 bg-white/95 backdrop-blur-md border-b border-slate-200 shadow-xs">
        <div className="max-w-7xl mx-auto px-4 h-20 flex items-center justify-between gap-6">
          
          <div className="flex items-center gap-3 cursor-pointer select-none" onClick={() => setActiveTab('home')}>
            <div className="w-11 h-11 rounded-2xl bg-gradient-to-tr from-indigo-600 to-cyan-500 p-0.5 shadow-md shadow-indigo-500/20">
              <div className="w-full h-full bg-white rounded-[14px] flex items-center justify-center">
                <Cpu className="w-6 h-6 text-indigo-600" />
              </div>
            </div>
            <div>
              <div className="text-2xl font-black tracking-tight leading-none text-slate-900">
                PC<span className="text-indigo-600">STORE</span>
              </div>
              <span className="text-[10px] tracking-widest text-cyan-600 uppercase font-black">
                PostgreSQL Live Database
              </span>
            </div>
          </div>

          {/* Ô tìm kiếm Topbar */}
          <div className="flex-1 max-w-lg relative hidden md:block">
            <input
              type="text"
              placeholder="Tìm kiếm linh kiện theo tên, thương hiệu..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full bg-slate-100/90 border border-slate-200 rounded-2xl pl-11 pr-24 py-2.5 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800 transition"
            />
            <Search className="w-4 h-4 text-slate-400 absolute left-4 top-3" />
            {searchTerm && (
              <button 
                onClick={() => setSearchTerm('')} 
                className="absolute right-12 top-2.5 text-slate-400 hover:text-slate-600"
              >
                <X className="w-4 h-4" />
              </button>
            )}
            <button 
              onClick={() => {
                setActiveTab('home');
                document.getElementById('catalog-section')?.scrollIntoView({ behavior: 'smooth' });
              }}
              className="absolute right-1.5 top-1.5 bottom-1.5 px-4 bg-indigo-600 hover:bg-indigo-700 text-white font-bold text-xs rounded-xl shadow transition"
            >
              Tìm
            </button>
          </div>

          <nav className="flex items-center gap-2">
            <button 
              onClick={() => setActiveTab('home')}
              className={`px-3 py-2 rounded-2xl text-xs font-bold transition ${
                activeTab === 'home' ? 'bg-slate-900 text-white' : 'text-slate-600 hover:bg-slate-100'
              }`}
            >
              Trang Chủ
            </button>

            <button 
              onClick={() => setActiveTab('builder')}
              className={`flex items-center gap-1.5 px-3 py-2 rounded-2xl text-xs font-bold transition ${
                activeTab === 'builder' ? 'bg-indigo-600 text-white shadow-indigo-500/25' : 'bg-indigo-50 hover:bg-indigo-100 text-indigo-700 border border-indigo-200'
              }`}
            >
              <Wrench className="w-4 h-4" />
              <span>Ráp PC</span>
            </button>

            {/* Tài khoản */}
            {currentUser ? (
              <div className="flex items-center gap-2 px-3 py-1.5 bg-emerald-50 border border-emerald-200 rounded-2xl">
                <button 
                  onClick={handleOpenProfile}
                  className="flex items-center gap-2 text-left hover:opacity-80 transition"
                  title="Xem và sửa hồ sơ cá nhân"
                >
                  <div className="w-6 h-6 rounded-full bg-emerald-600 text-white flex items-center justify-center text-[10px] font-black uppercase">
                    {currentUser.fullName ? currentUser.fullName.charAt(0) : 'U'}
                  </div>
                  <div>
                    <p className="text-[11px] font-bold text-emerald-900 leading-tight max-w-[90px] truncate">
                      {currentUser.fullName}
                    </p>
                    <p className="text-[9px] text-emerald-600 font-medium leading-none">
                      Hồ sơ cá nhân
                    </p>
                  </div>
                </button>
                <button 
                  onClick={handleLogout} 
                  title="Đăng xuất tài khoản"
                  className="p-1.5 hover:bg-rose-100 rounded-xl text-slate-400 hover:text-rose-600 transition ml-1"
                >
                  <LogOut className="w-4 h-4" />
                </button>
              </div>
            ) : (
              <div className="flex items-center gap-1.5">
                <button 
                  onClick={() => setIsLoginOpen(true)}
                  className="flex items-center gap-1.5 px-3 py-2 rounded-2xl bg-slate-100 hover:bg-slate-200 border border-slate-200 text-slate-800 text-xs font-bold transition"
                >
                  <LogIn className="w-3.5 h-3.5 text-slate-600" />
                  <span>Đăng Nhập</span>
                </button>
                <button 
                  onClick={() => setIsRegisterOpen(true)}
                  className="flex items-center gap-1.5 px-3 py-2 rounded-2xl bg-indigo-600 hover:bg-indigo-700 text-white text-xs font-bold transition shadow-xs"
                >
                  <UserPlus className="w-3.5 h-3.5" />
                  <span>Đăng Ký</span>
                </button>
              </div>
            )}

            <button className="flex items-center gap-1.5 px-3 py-2 rounded-2xl bg-slate-100 hover:bg-slate-200 border border-slate-200 text-slate-700 text-xs font-bold relative">
              <ShoppingCart className="w-4 h-4 text-slate-700" />
              {Object.keys(selectedParts).length > 0 && (
                <span className="bg-rose-500 text-white rounded-full px-1.5 py-0.2 text-[10px] font-bold">
                  {Object.keys(selectedParts).length}
                </span>
              )}
            </button>
          </nav>
        </div>
      </header>

      {/* 3. MAIN CONTENT */}
      <main className="flex-1 max-w-7xl mx-auto px-4 w-full py-6">
        {apiError ? (
          <div className="bg-rose-50 border border-rose-200 rounded-3xl p-8 text-center space-y-4 max-w-xl mx-auto my-12 shadow-xs">
            <div className="w-12 h-12 bg-rose-100 text-rose-600 rounded-2xl flex items-center justify-center mx-auto">
              <AlertTriangle className="w-6 h-6" />
            </div>
            <h3 className="text-base font-bold text-rose-900">Lỗi kết nối Backend API</h3>
            <p className="text-xs text-rose-700">{apiError}</p>
            <button onClick={fetchProducts} className="inline-flex items-center gap-2 px-5 py-2.5 bg-rose-600 hover:bg-rose-700 text-white font-bold text-xs rounded-xl shadow transition">
              <RefreshCw className="w-3.5 h-3.5" />
              <span>Thử nạp lại dữ liệu</span>
            </button>
          </div>
        ) : activeTab === 'home' ? (
          <div className="space-y-12">
            
            {/* HERO SECTION */}
            <div className="grid grid-cols-1 lg:grid-cols-4 gap-6 items-start">
              
              <div className="bg-white border border-slate-200 rounded-3xl p-3 space-y-1 hidden lg:block shadow-xs">
                <div className="px-3 py-2 text-xs font-black text-indigo-600 uppercase tracking-wider border-b border-slate-100 flex items-center gap-2">
                  <Layers className="w-4 h-4" /> Danh Mục ({categories.length})
                </div>
                {categories.map((c) => (
                  <div
                    key={c.id}
                    onClick={() => {
                      setSelectedCategoryTab(c.type);
                      document.getElementById('catalog-section')?.scrollIntoView({ behavior: 'smooth' });
                    }}
                    className={`flex items-center justify-between px-3 py-2.5 rounded-2xl cursor-pointer text-xs font-semibold transition group ${
                      selectedCategoryTab === c.type ? 'bg-indigo-50 text-indigo-600' : 'text-slate-600 hover:bg-slate-100 hover:text-indigo-600'
                    }`}
                  >
                    <span>{c.name}</span>
                    <ChevronRight className="w-3.5 h-3.5 text-slate-400 group-hover:text-indigo-600 group-hover:translate-x-0.5 transition" />
                  </div>
                ))}
              </div>

              <div className="lg:col-span-3 space-y-4">
                <div className="relative overflow-hidden rounded-3xl bg-gradient-to-r from-indigo-900 via-indigo-800 to-slate-900 border border-indigo-800 p-8 md:p-10 shadow-xl text-white flex flex-col justify-between min-h-[280px]">
                  <div className="space-y-4 max-w-xl z-10">
                    <span className="inline-flex items-center gap-1.5 px-3 py-1 rounded-full bg-cyan-400/20 border border-cyan-400/30 text-cyan-300 text-xs font-black">
                      <Sparkles className="w-3.5 h-3.5" /> SMART PC COMPATIBILITY ENGINE 2026
                    </span>
                    <h1 className="text-3xl md:text-4xl font-black text-white leading-tight">
                      Linh Kiện Máy Tính Thực Tế <br />
                      <span className="text-transparent bg-clip-text bg-gradient-to-r from-cyan-300 via-teal-200 to-amber-300">
                        100% Từ Cơ Sở Dữ Liệu PostgreSQL
                      </span>
                    </h1>
                    <p className="text-xs md:text-sm text-indigo-100/90 leading-relaxed">
                      Toàn bộ linh kiện và thông số kỹ thuật (Socket, DDR5, Bus RAM, TDP) được truy vấn trực tiếp từ cơ sở dữ liệu PostgreSQL.
                    </p>
                    <div className="flex gap-3 pt-1">
                      <button 
                        onClick={() => setActiveTab('builder')}
                        className="px-6 py-3 bg-gradient-to-r from-cyan-400 to-teal-400 hover:from-cyan-300 hover:to-teal-300 text-slate-950 font-black text-xs rounded-2xl shadow-lg shadow-cyan-400/25 transition flex items-center gap-2"
                      >
                        <Wrench className="w-4 h-4 text-slate-950" /> Bắt đầu Ráp PC Ngay
                      </button>
                    </div>
                  </div>
                </div>

                <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
                  <div className="bg-white border border-slate-200/80 rounded-2xl p-4 flex items-center gap-3 shadow-xs">
                    <div className="p-2.5 rounded-xl bg-indigo-50 text-indigo-600"><Cpu className="w-5 h-5" /></div>
                    <div>
                      <h4 className="text-xs font-bold text-slate-900">100% Chính Hãng</h4>
                      <p className="text-[11px] text-slate-500">Bảo hành 36 tháng 1 đổi 1</p>
                    </div>
                  </div>
                  <div className="bg-white border border-slate-200/80 rounded-2xl p-4 flex items-center gap-3 shadow-xs">
                    <div className="p-2.5 rounded-xl bg-cyan-50 text-cyan-600"><Monitor className="w-5 h-5" /></div>
                    <div>
                      <h4 className="text-xs font-bold text-slate-900">VGA RTX 40 Series</h4>
                      <p className="text-[11px] text-slate-500">Tối ưu 3D & Gaming</p>
                    </div>
                  </div>
                  <div className="bg-white border border-slate-200/80 rounded-2xl p-4 flex items-center gap-3 shadow-xs">
                    <div className="p-2.5 rounded-xl bg-emerald-50 text-emerald-600"><Zap className="w-5 h-5" /></div>
                    <div>
                      <h4 className="text-xs font-bold text-slate-900">Nguồn Chuẩn 80 Plus</h4>
                      <p className="text-[11px] text-slate-500">Tính công suất TDP tự động</p>
                    </div>
                  </div>
                </div>
              </div>

            </div>

            {/* DANH SÁCH LINH KIỆN & BỘ LỌC TÌM KIẾM */}
            <section id="catalog-section" className="space-y-6">
              
              {/* Header danh mục & Đếm */}
              <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4">
                <div>
                  <h2 className="text-xl font-black text-slate-900 tracking-tight uppercase">
                    Kho Linh Kiện Trong Cơ Sở Dữ Liệu ({products.length} sản phẩm)
                  </h2>
                  <p className="text-xs text-slate-500">Dữ liệu thời gian thực từ bảng Products trong PostgreSQL</p>
                </div>
                
                {/* Tabs phân loại danh mục */}
                <div className="flex items-center gap-2 overflow-x-auto pb-1 max-w-full">
                  <button
                    onClick={() => setSelectedCategoryTab('ALL')}
                    className={`px-4 py-2 rounded-2xl text-xs font-bold transition whitespace-nowrap ${
                      selectedCategoryTab === 'ALL' 
                        ? 'bg-indigo-600 text-white shadow-md shadow-indigo-500/20' 
                        : 'bg-white border border-slate-200 text-slate-600 hover:bg-slate-100'
                    }`}
                  >
                    Tất Cả
                  </button>
                  {categories.map(cat => (
                    <button
                      key={cat.id}
                      onClick={() => setSelectedCategoryTab(cat.type)}
                      className={`px-4 py-2 rounded-2xl text-xs font-bold transition whitespace-nowrap ${
                        selectedCategoryTab?.toLowerCase() === cat.type?.toLowerCase()
                          ? 'bg-indigo-600 text-white shadow-md shadow-indigo-500/20' 
                          : 'bg-white border border-slate-200 text-slate-600 hover:bg-slate-100'
                      }`}
                    >
                      {cat.name}
                    </button>
                  ))}
                </div>
              </div>

              {/* THANH CÔNG CỤ LỌC NÂNG CAO (Search, Khoảng Giá, Sắp Xếp) */}
              <div className="bg-white border border-slate-200 rounded-3xl p-4 shadow-xs flex flex-wrap items-center justify-between gap-4">
                <div className="flex flex-wrap items-center gap-3">
                  
                  {/* Tìm kiếm inline */}
                  <div className="relative min-w-[220px]">
                    <Search className="w-3.5 h-3.5 text-slate-400 absolute left-3.5 top-3" />
                    <input
                      type="text"
                      placeholder="Lọc tên linh kiện, hãng..."
                      value={searchTerm}
                      onChange={(e) => setSearchTerm(e.target.value)}
                      className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-9 pr-3 py-2 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                    />
                  </div>

                  {/* Lọc khoảng giá */}
                  <div className="flex items-center gap-1.5 bg-slate-50 border border-slate-200 rounded-xl px-3 py-1.5">
                    <SlidersHorizontal className="w-3.5 h-3.5 text-slate-500" />
                    <span className="text-[11px] font-bold text-slate-600">Giá:</span>
                    <select
                      value={priceRange}
                      onChange={(e) => setPriceRange(e.target.value)}
                      className="bg-transparent text-xs font-bold text-slate-800 focus:outline-none cursor-pointer"
                    >
                      <option value="ALL">Mọi mức giá</option>
                      <option value="UNDER_5M">Dưới 5 triệu</option>
                      <option value="5M_15M">Từ 5 - 15 triệu</option>
                      <option value="OVER_15M">Trên 15 triệu</option>
                    </select>
                  </div>

                  {/* Sắp xếp giá */}
                  <div className="flex items-center gap-1.5 bg-slate-50 border border-slate-200 rounded-xl px-3 py-1.5">
                    <ArrowUpDown className="w-3.5 h-3.5 text-slate-500" />
                    <span className="text-[11px] font-bold text-slate-600">Sắp xếp:</span>
                    <select
                      value={sortBy}
                      onChange={(e) => setSortBy(e.target.value)}
                      className="bg-transparent text-xs font-bold text-slate-800 focus:outline-none cursor-pointer"
                    >
                      <option value="default">Mặc định</option>
                      <option value="price_asc">Giá: Thấp đến Cao</option>
                      <option value="price_desc">Giá: Cao đến Thấp</option>
                    </select>
                  </div>
                </div>

                {/* Nút Reset bộ lọc */}
                {(searchTerm || selectedCategoryTab !== 'ALL' || priceRange !== 'ALL' || sortBy !== 'default') && (
                  <button
                    onClick={handleResetFilters}
                    className="flex items-center gap-1 text-xs font-bold text-rose-600 hover:text-rose-700 bg-rose-50 hover:bg-rose-100 px-3 py-2 rounded-xl transition"
                  >
                    <RotateCcw className="w-3.5 h-3.5" />
                    <span>Xóa bộ lọc</span>
                  </button>
                )}
              </div>

              {/* Danh sách hiển thị sản phẩm */}
              {loading ? (
                <div className="flex flex-col items-center justify-center py-20 space-y-3">
                  <Loader2 className="w-8 h-8 text-indigo-600 animate-spin" />
                  <p className="text-xs font-semibold text-slate-500">Đang lọc dữ liệu sản phẩm...</p>
                </div>
              ) : products.length === 0 ? (
                <div className="bg-white border border-slate-200 rounded-3xl p-12 text-center text-slate-400 space-y-3">
                  <Filter className="w-12 h-12 mx-auto text-slate-300" />
                  <p className="text-sm font-bold text-slate-600">Không tìm thấy linh kiện nào phù hợp.</p>
                  <p className="text-xs">Hãy thử từ khóa khác hoặc xóa bớt các điều kiện lọc giá/danh mục.</p>
                  <button
                    onClick={handleResetFilters}
                    className="px-4 py-2 bg-indigo-50 hover:bg-indigo-100 text-indigo-600 font-bold text-xs rounded-xl transition"
                  >
                    Hiển thị tất cả sản phẩm
                  </button>
                </div>
              ) : (
                <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-5">
                  {products.map((p) => (
                    <div key={p.id} className="bg-white border border-slate-200 hover:border-indigo-400 rounded-3xl p-4 flex flex-col justify-between space-y-4 transition hover:shadow-lg shadow-xs group">
                      <div className="space-y-3 cursor-pointer" onClick={() => handleOpenDetail(p.id)}>
                        <div className="w-full h-40 bg-slate-50 rounded-2xl flex flex-col items-center justify-center p-4 relative border border-slate-100">
                          <span className="absolute top-2 left-2 text-[10px] font-bold text-slate-600 bg-white border border-slate-200 px-2 py-0.5 rounded-md">
                            {p.brand}
                          </span>
                          <div className="p-3.5 bg-white rounded-2xl shadow-xs border border-slate-100 group-hover:scale-105 transition">
                            {p.imageUrl && p.imageUrl.startsWith('http') ? (
                              <img src={p.imageUrl} alt={p.name} className="w-16 h-16 object-contain" />
                            ) : (
                              renderProductIcon(p.categoryType)
                            )}
                          </div>
                          <span className="text-[10px] font-semibold text-slate-400 mt-2 uppercase tracking-wider">
                            {p.categoryType}
                          </span>
                        </div>
                        
                        <div className="flex gap-1 flex-wrap text-[10px]">
                          {p.socket && <span className="bg-indigo-50 text-indigo-700 px-2 py-0.5 rounded-md font-semibold">Socket {p.socket}</span>}
                          {p.ramType && <span className="bg-cyan-50 text-cyan-700 px-2 py-0.5 rounded-md font-semibold">{p.ramType}</span>}
                          {p.tdpWattage > 0 && <span className="bg-amber-50 text-amber-700 px-2 py-0.5 rounded-md font-semibold">{p.tdpWattage}W TDP</span>}
                        </div>

                        <h3 className="text-xs font-bold text-slate-800 line-clamp-2 leading-snug group-hover:text-indigo-600 transition">{p.name}</h3>
                      </div>

                      <div className="pt-2 border-t border-slate-100 flex items-center justify-between">
                        <div>
                          <p className="text-sm font-black text-indigo-600">{p.price?.toLocaleString('vi-VN')} đ</p>
                          <p className="text-[10px] text-slate-400">Kho: {p.stockQuantity || 0} chiếc</p>
                        </div>
                        <div className="flex items-center gap-1.5">
                          <button 
                            onClick={() => handleOpenDetail(p.id)}
                            className="p-2.5 bg-slate-50 hover:bg-slate-100 text-slate-600 rounded-xl transition"
                            title="Xem chi tiết"
                          >
                            <Eye className="w-4 h-4" />
                          </button>
                          <button 
                            onClick={() => {
                              handleSelectPart(p);
                              setActiveTab('builder');
                            }}
                            className="p-2.5 bg-indigo-50 hover:bg-indigo-600 hover:text-white text-indigo-600 rounded-xl transition"
                            title="Thêm vào dàn PC"
                          >
                            <Plus className="w-4 h-4" />
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </section>

          </div>
        ) : (
          /* TRANG PC BUILDER */
          <div className="py-2 grid grid-cols-1 lg:grid-cols-3 gap-8">
            <div className="lg:col-span-2 space-y-4">
              <div className="flex items-center justify-between pb-3 border-b border-slate-200">
                <div>
                  <h2 className="text-lg font-black text-slate-900 uppercase">Cấu hình PC tự chọn</h2>
                  <p className="text-xs text-slate-500">Đối chiếu trực tiếp Socket và công suất từ DB PostgreSQL</p>
                </div>
                <button 
                  onClick={() => setSelectedParts({})}
                  className="text-xs text-rose-600 hover:underline font-bold"
                >
                  Xóa tất cả linh kiện
                </button>
              </div>

              {categories.length === 0 ? (
                <div className="bg-white border border-slate-200 rounded-2xl p-6 text-center text-slate-400 text-xs">
                  Không tìm thấy danh mục linh kiện trong Database.
                </div>
              ) : (
                categories.map((cat) => {
                  const part = selectedParts[cat.type];
                  return (
                    <div key={cat.id} className="bg-white border border-slate-200 rounded-2xl p-4 flex items-center justify-between gap-4 shadow-xs">
                      <div className="flex items-center gap-3.5 min-w-0">
                        <div className="w-12 h-12 rounded-xl bg-slate-100 flex items-center justify-center text-slate-400 font-bold shrink-0 text-xs border border-slate-200">
                          {part ? renderProductIcon(part.categoryType) : <Cpu className="w-5 h-5 text-slate-400" />}
                        </div>
                        <div className="truncate">
                          <p className="text-[11px] text-indigo-600 font-bold uppercase">{cat.name}</p>
                          {part ? (
                            <p className="text-xs font-bold text-slate-800 truncate">{part.name}</p>
                          ) : (
                            <p className="text-xs text-slate-400 italic">Vui lòng bấm chọn linh kiện...</p>
                          )}
                        </div>
                      </div>

                      <div className="flex items-center gap-3 shrink-0">
                        {part ? (
                          <>
                            <span className="text-xs font-black text-indigo-600">{part.price?.toLocaleString('vi-VN')} đ</span>
                            <button onClick={() => handleRemovePart(cat.type)} className="p-2 hover:bg-rose-50 text-rose-500 rounded-xl transition" title="Xóa">
                              <Trash2 className="w-4 h-4" />
                            </button>
                          </>
                        ) : (
                          <button 
                            onClick={() => setModalCategory(cat)}
                            className="flex items-center gap-1 px-4 py-2 rounded-xl bg-indigo-50 hover:bg-indigo-600 hover:text-white text-indigo-600 text-xs font-bold transition border border-indigo-200 shadow-xs"
                          >
                            <Plus className="w-3.5 h-3.5" /> Chọn
                          </button>
                        )}
                      </div>
                    </div>
                  );
                })
              )}
            </div>

            <div className="space-y-6">
              <div className="bg-white border border-slate-200 rounded-3xl p-6 sticky top-28 space-y-6 shadow-sm">
                <h3 className="text-base font-black text-slate-900 uppercase border-b border-slate-100 pb-3">Chi tiết & Tương thích</h3>
                
                <div className="space-y-2">
                  <div className="flex justify-between text-xs font-bold">
                    <span className="text-amber-600 flex items-center gap-1"><Zap className="w-4 h-4" /> Công suất dự tính</span>
                    <span className="text-slate-700">{estimatedTdp}W</span>
                  </div>
                  <div className="w-full bg-slate-100 h-2.5 rounded-full overflow-hidden border border-slate-200">
                    <div 
                      className={`h-full transition-all ${estimatedTdp > (psuWattage || 500) ? 'bg-rose-500' : 'bg-emerald-500'}`} 
                      style={{ width: `${Math.min((estimatedTdp / (psuWattage || 650)) * 100, 100)}%` }}
                    />
                  </div>
                  {psuWattage > 0 && <p className="text-[11px] text-slate-500">Nguồn đang chọn: <strong className="text-slate-800">{psuWattage}W</strong></p>}
                </div>

                <div className="space-y-2">
                  <p className="text-xs font-bold text-slate-700 uppercase">Trạng thái kỹ thuật</p>
                  {compatibilityErrors.length > 0 ? (
                    compatibilityErrors.map((err, idx) => (
                      <div key={idx} className="flex gap-2 p-3 bg-rose-50 border border-rose-200 rounded-2xl text-rose-600 text-xs items-start">
                        <AlertCircle className="w-4 h-4 shrink-0 mt-0.5" />
                        <span>{err}</span>
                      </div>
                    ))
                  ) : (
                    <div className="flex gap-2 p-3 bg-emerald-50 border border-emerald-200 rounded-2xl text-emerald-700 text-xs items-center">
                      <CheckCircle2 className="w-4 h-4 shrink-0" />
                      <span>Các linh kiện hoàn toàn tương thích!</span>
                    </div>
                  )}
                </div>

                <div className="border-t border-slate-100 pt-4 space-y-4">
                  <div className="flex justify-between items-baseline">
                    <span className="text-xs text-slate-500 uppercase font-bold">Tổng thanh toán:</span>
                    <span className="text-xl font-black text-indigo-600">{totalPrice.toLocaleString('vi-VN')} đ</span>
                  </div>
                  <button 
                    disabled={compatibilityErrors.length > 0 || totalPrice === 0}
                    className="w-full py-3.5 rounded-2xl bg-indigo-600 hover:bg-indigo-700 disabled:opacity-40 text-white font-black text-xs shadow-lg shadow-indigo-500/25 transition uppercase tracking-wider"
                  >
                    Thêm vào giỏ hàng & Đặt mua
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}
      </main>

      {/* 4. MODAL CHỌN LINH KIỆN CHO BUILDER */}
      {modalCategory && (
        <div className="fixed inset-0 z-50 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="bg-white border border-slate-200 rounded-3xl max-w-2xl w-full max-h-[80vh] flex flex-col shadow-2xl">
            <div className="p-4 border-b border-slate-100 flex justify-between items-center bg-slate-50 rounded-t-3xl">
              <h3 className="text-sm font-bold text-slate-900 uppercase">Chọn {modalCategory.name}</h3>
              <button onClick={() => setModalCategory(null)} className="p-1 hover:bg-slate-200 rounded-xl text-slate-500">
                <X className="w-5 h-5" />
              </button>
            </div>
            
            <div className="p-4 overflow-y-auto space-y-3 flex-1">
              {products.filter(p => p.categoryType?.toLowerCase() === modalCategory.type?.toLowerCase()).length === 0 ? (
                <div className="text-center py-10 text-slate-400 text-xs">
                  Không tìm thấy linh kiện phù hợp trong kho cơ sở dữ liệu.
                </div>
              ) : (
                products.filter(p => p.categoryType?.toLowerCase() === modalCategory.type?.toLowerCase()).map(product => (
                  <div key={product.id} className="p-3 bg-slate-50 hover:bg-indigo-50/50 border border-slate-200 rounded-2xl flex items-center justify-between gap-4 transition">
                    <div className="flex items-center gap-3">
                      <div className="w-12 h-12 bg-white rounded-xl p-2 border border-slate-200 flex items-center justify-center">
                        {renderProductIcon(product.categoryType)}
                      </div>
                      <div>
                        <p className="text-xs font-bold text-slate-900">{product.name}</p>
                        <p className="text-[11px] text-slate-500">
                          Hãng: {product.brand} {product.socket ? `| Socket: ${product.socket}` : ''} {product.ramType ? `| RAM: ${product.ramType}` : ''}
                        </p>
                      </div>
                    </div>
                    <div className="text-right shrink-0">
                      <p className="text-indigo-600 font-bold text-xs mb-1">{product.price?.toLocaleString('vi-VN')} đ</p>
                      <button onClick={() => handleSelectPart(product)} className="px-3.5 py-1.5 bg-indigo-600 text-white text-xs font-bold rounded-xl hover:bg-indigo-700 shadow">
                        Chọn
                      </button>
                    </div>
                  </div>
                ))
              )}
            </div>
          </div>
        </div>
      )}

      {/* 5. MODAL HỒ SƠ CÁ NHÂN */}
      {isProfileOpen && (
        <div className="fixed inset-0 z-50 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="bg-white border border-slate-200 rounded-3xl max-w-md w-full p-6 shadow-2xl relative animate-in fade-in duration-150">
            <button onClick={() => setIsProfileOpen(false)} className="absolute top-5 right-5 p-1.5 rounded-full hover:bg-slate-100 text-slate-400 hover:text-slate-700">
              <X className="w-5 h-5" />
            </button>

            <div className="flex items-center gap-3 mb-6">
              <div className="p-2.5 rounded-2xl bg-indigo-50 text-indigo-600">
                <Settings className="w-6 h-6" />
              </div>
              <div>
                <h2 className="text-xl font-black text-slate-900">Hồ Sơ Khách Hàng</h2>
                <p className="text-xs text-slate-500">{currentUser?.email}</p>
              </div>
            </div>

            {profileError && (
              <div className="mb-4 p-3 rounded-2xl bg-rose-50 border border-rose-200 text-rose-600 text-xs flex items-center gap-2">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{profileError}</span>
              </div>
            )}

            {profileSuccess && (
              <div className="mb-4 p-3 rounded-2xl bg-emerald-50 border border-emerald-200 text-emerald-700 text-xs flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 shrink-0" />
                <span>{profileSuccess}</span>
              </div>
            )}

            <form onSubmit={handleProfileSubmit} className="space-y-3.5">
              <div>
                <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Họ và Tên</label>
                <div className="relative">
                  <input
                    type="text"
                    required
                    value={profileForm.fullName}
                    onChange={(e) => setProfileForm({ ...profileForm, fullName: e.target.value })}
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-10 pr-4 py-2 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                  />
                  <User className="w-4 h-4 text-slate-400 absolute left-3.5 top-2.5" />
                </div>
              </div>

              <div>
                <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Số điện thoại</label>
                <div className="relative">
                  <input
                    type="tel"
                    value={profileForm.phoneNumber}
                    onChange={(e) => setProfileForm({ ...profileForm, phoneNumber: e.target.value })}
                    placeholder="0905..."
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-10 pr-4 py-2 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                  />
                  <Phone className="w-4 h-4 text-slate-400 absolute left-3.5 top-2.5" />
                </div>
              </div>

              <div>
                <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Địa chỉ giao hàng</label>
                <div className="relative">
                  <input
                    type="text"
                    value={profileForm.address}
                    onChange={(e) => setProfileForm({ ...profileForm, address: e.target.value })}
                    placeholder="Địa chỉ tại Đà Nẵng..."
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-10 pr-4 py-2 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                  />
                  <MapPin className="w-4 h-4 text-slate-400 absolute left-3.5 top-2.5" />
                </div>
              </div>

              <div className="border-t border-slate-100 pt-3 space-y-2">
                <p className="text-[11px] font-bold text-slate-700 uppercase flex items-center gap-1.5">
                  <KeyRound className="w-3.5 h-3.5 text-indigo-600" /> Đổi mật khẩu (Bỏ trống nếu không đổi)
                </p>
                <div className="grid grid-cols-2 gap-2">
                  <input
                    type="password"
                    placeholder="Mật khẩu hiện tại"
                    value={profileForm.currentPassword}
                    onChange={(e) => setProfileForm({ ...profileForm, currentPassword: e.target.value })}
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl px-3 py-2 text-xs focus:outline-none focus:border-indigo-500"
                  />
                  <input
                    type="password"
                    placeholder="Mật khẩu mới (≥6 ký tự)"
                    value={profileForm.newPassword}
                    onChange={(e) => setProfileForm({ ...profileForm, newPassword: e.target.value })}
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl px-3 py-2 text-xs focus:outline-none focus:border-indigo-500"
                  />
                </div>
              </div>

              <div className="pt-3">
                <button
                  type="submit"
                  disabled={profileLoading}
                  className="w-full py-2.5 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 text-white font-bold text-xs rounded-xl shadow transition flex items-center justify-center gap-2"
                >
                  {profileLoading ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      <span>Đang lưu thay đổi...</span>
                    </>
                  ) : (
                    <span>Lưu Cập Nhật Hồ Sơ</span>
                  )}
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* 6. MODAL ĐĂNG NHẬP */}
      {isLoginOpen && (
        <div className="fixed inset-0 z-50 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="bg-white border border-slate-200 rounded-3xl max-w-md w-full p-6 shadow-2xl relative">
            <button onClick={() => setIsLoginOpen(false)} className="absolute top-5 right-5 p-1.5 rounded-full hover:bg-slate-100 text-slate-400 hover:text-slate-700">
              <X className="w-5 h-5" />
            </button>

            <div className="flex items-center gap-3 mb-6">
              <div className="p-2.5 rounded-2xl bg-indigo-50 text-indigo-600">
                <LogIn className="w-6 h-6" />
              </div>
              <div>
                <h2 className="text-xl font-black text-slate-900">Đăng Nhập</h2>
                <p className="text-xs text-slate-500">Đăng nhập tài khoản khách hàng PCSTORE</p>
              </div>
            </div>

            {loginError && (
              <div className="mb-4 p-3 rounded-2xl bg-rose-50 border border-rose-200 text-rose-600 text-xs flex items-center gap-2">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{loginError}</span>
              </div>
            )}

            {loginSuccess && (
              <div className="mb-4 p-3 rounded-2xl bg-emerald-50 border border-emerald-200 text-emerald-700 text-xs flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 shrink-0" />
                <span>{loginSuccess}</span>
              </div>
            )}

            <form onSubmit={handleLoginSubmit} className="space-y-3.5">
              <div>
                <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Email</label>
                <div className="relative">
                  <input
                    type="email"
                    required
                    value={loginForm.email}
                    onChange={(e) => setLoginForm({ ...loginForm, email: e.target.value })}
                    placeholder="customer@pcstore.vn"
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-10 pr-4 py-2.5 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                  />
                  <Mail className="w-4 h-4 text-slate-400 absolute left-3.5 top-3" />
                </div>
              </div>

              <div>
                <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Mật khẩu</label>
                <div className="relative">
                  <input
                    type="password"
                    required
                    value={loginForm.password}
                    onChange={(e) => setLoginForm({ ...loginForm, password: e.target.value })}
                    placeholder="••••••••"
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-10 pr-4 py-2.5 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                  />
                  <Lock className="w-4 h-4 text-slate-400 absolute left-3.5 top-3" />
                </div>
              </div>

              <div className="pt-2">
                <button
                  type="submit"
                  disabled={loginLoading}
                  className="w-full py-2.5 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 text-white font-bold text-xs rounded-xl shadow transition flex items-center justify-center gap-2"
                >
                  {loginLoading ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      <span>Đang xác thực...</span>
                    </>
                  ) : (
                    <span>Đăng Nhập Ngay</span>
                  )}
                </button>
              </div>

              <div className="text-center pt-2 text-xs text-slate-500">
                Chưa có tài khoản?{' '}
                <button type="button" onClick={() => { setIsLoginOpen(false); setIsRegisterOpen(true); }} className="text-indigo-600 font-bold hover:underline">
                  Đăng ký tài khoản mới
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* 7. MODAL ĐĂNG KÝ */}
      {isRegisterOpen && (
        <div className="fixed inset-0 z-50 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="bg-white border border-slate-200 rounded-3xl max-w-md w-full p-6 shadow-2xl relative">
            <button onClick={() => setIsRegisterOpen(false)} className="absolute top-5 right-5 p-1.5 rounded-full hover:bg-slate-100 text-slate-400 hover:text-slate-700">
              <X className="w-5 h-5" />
            </button>

            <div className="flex items-center gap-3 mb-6">
              <div className="p-2.5 rounded-2xl bg-indigo-50 text-indigo-600">
                <UserPlus className="w-6 h-6" />
              </div>
              <div>
                <h2 className="text-xl font-black text-slate-900">Đăng Ký Tài Khoản</h2>
                <p className="text-xs text-slate-500">Tạo tài khoản khách hàng PCSTORE</p>
              </div>
            </div>

            {registerError && (
              <div className="mb-4 p-3 rounded-2xl bg-rose-50 border border-rose-200 text-rose-600 text-xs flex items-center gap-2">
                <AlertCircle className="w-4 h-4 shrink-0" />
                <span>{registerError}</span>
              </div>
            )}

            {registerSuccess && (
              <div className="mb-4 p-3 rounded-2xl bg-emerald-50 border border-emerald-200 text-emerald-700 text-xs flex items-center gap-2">
                <CheckCircle2 className="w-4 h-4 shrink-0" />
                <span>{registerSuccess}</span>
              </div>
            )}

            <form onSubmit={handleRegisterSubmit} className="space-y-3.5">
              <div>
                <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Họ và Tên</label>
                <div className="relative">
                  <input
                    type="text"
                    required
                    value={registerForm.fullName}
                    onChange={(e) => setRegisterForm({ ...registerForm, fullName: e.target.value })}
                    placeholder="Nguyễn Văn A"
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-10 pr-4 py-2 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                  />
                  <User className="w-4 h-4 text-slate-400 absolute left-3.5 top-2.5" />
                </div>
              </div>

              <div>
                <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Email</label>
                <div className="relative">
                  <input
                    type="email"
                    required
                    value={registerForm.email}
                    onChange={(e) => setRegisterForm({ ...registerForm, email: e.target.value })}
                    placeholder="customer@pcstore.vn"
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-10 pr-4 py-2 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                  />
                  <Mail className="w-4 h-4 text-slate-400 absolute left-3.5 top-2.5" />
                </div>
              </div>

              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Mật khẩu</label>
                  <div className="relative">
                    <input
                      type="password"
                      required
                      value={registerForm.password}
                      onChange={(e) => setRegisterForm({ ...registerForm, password: e.target.value })}
                      placeholder="••••••••"
                      className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-9 pr-3 py-2 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                    />
                    <Lock className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
                  </div>
                </div>

                <div>
                  <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Xác nhận MK</label>
                  <div className="relative">
                    <input
                      type="password"
                      required
                      value={registerForm.confirmPassword}
                      onChange={(e) => setRegisterForm({ ...registerForm, confirmPassword: e.target.value })}
                      placeholder="••••••••"
                      className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-9 pr-3 py-2 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                    />
                    <Lock className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
                  </div>
                </div>
              </div>

              <div>
                <label className="block text-[11px] font-bold text-slate-600 uppercase mb-1">Số điện thoại</label>
                <div className="relative">
                  <input
                    type="tel"
                    value={registerForm.phoneNumber}
                    onChange={(e) => setRegisterForm({ ...registerForm, phoneNumber: e.target.value })}
                    placeholder="0905123456"
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl pl-10 pr-4 py-2 text-xs focus:outline-none focus:border-indigo-500 focus:bg-white text-slate-800"
                  />
                  <Phone className="w-4 h-4 text-slate-400 absolute left-3.5 top-2.5" />
                </div>
              </div>

              <div className="pt-2">
                <button
                  type="submit"
                  disabled={registerLoading}
                  className="w-full py-2.5 bg-indigo-600 hover:bg-indigo-700 disabled:opacity-50 text-white font-bold text-xs rounded-xl shadow transition flex items-center justify-center gap-2"
                >
                  {registerLoading ? (
                    <>
                      <Loader2 className="w-4 h-4 animate-spin" />
                      <span>Đang đăng ký...</span>
                    </>
                  ) : (
                    <span>Tạo Tài Khoản Khách Hàng</span>
                  )}
                </button>
              </div>

              <div className="text-center pt-2 text-xs text-slate-500">
                Đã có tài khoản?{' '}
                <button type="button" onClick={() => { setIsRegisterOpen(false); setIsLoginOpen(true); }} className="text-indigo-600 font-bold hover:underline">
                  Đăng nhập ngay
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* 8. MODAL CHI TIẾT SẢN PHẨM (USE CASE PRODUCT DETAILS) */}
      <ProductDetailModal
        productId={selectedProductId}
        isOpen={isDetailOpen}
        onClose={() => {
          setIsDetailOpen(false);
          setSelectedProductId(null);
        }}
        onSelectToBuild={(prod) => {
          handleSelectPart(prod);
          setActiveTab('builder');
        }}
      />

      {/* 9. AI CHATBOT */}
      <div className="fixed bottom-6 right-6 z-50">
        {!isChatOpen ? (
          <button 
            onClick={() => setIsChatOpen(true)}
            className="flex items-center gap-2 px-5 py-3.5 rounded-full bg-gradient-to-r from-indigo-600 to-cyan-600 hover:from-indigo-700 hover:to-cyan-700 text-white font-black text-xs shadow-xl shadow-indigo-500/30 transition transform hover:scale-105"
          >
            <Bot className="w-5 h-5" />
            <span>AI Tư Vấn Phần Cứng</span>
          </button>
        ) : (
          <div className="bg-white border border-slate-200 rounded-3xl w-80 sm:w-96 shadow-2xl flex flex-col h-[480px] overflow-hidden">
            <div className="p-3.5 border-b border-slate-100 flex justify-between items-center bg-indigo-600 text-white">
              <div className="flex items-center gap-2">
                <Bot className="w-5 h-5 text-cyan-300" />
                <span className="font-bold text-xs uppercase tracking-wider">Trợ Lý AI PCSTORE</span>
              </div>
              <button onClick={() => setIsChatOpen(false)} className="p-1 hover:bg-indigo-700 rounded-lg text-white">
                <X className="w-4 h-4" />
              </button>
            </div>
            <div className="p-4 flex-1 overflow-y-auto space-y-3 text-xs bg-slate-50">
              {messages.map((m, i) => (
                <div key={i} className={`flex ${m.sender === 'user' ? 'justify-end' : 'justify-start'}`}>
                  <div className={`max-w-[85%] p-3 rounded-2xl ${m.sender === 'user' ? 'bg-indigo-600 text-white font-medium' : 'bg-white border border-slate-200 text-slate-800 shadow-xs'}`}>
                    {m.text}
                  </div>
                </div>
              ))}
            </div>
            <form onSubmit={handleSendMessage} className="p-3 border-t border-slate-100 flex gap-2 bg-white">
              <input 
                type="text" 
                value={chatInput} 
                onChange={(e) => setChatInput(e.target.value)} 
                placeholder="Hỏi AI tư vấn..." 
                className="flex-1 bg-slate-100 border border-slate-200 rounded-2xl px-3.5 py-2 text-xs focus:outline-none focus:border-indigo-500 text-slate-800"
              />
              <button type="submit" className="p-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-2xl font-bold shadow">
                <Send className="w-4 h-4" />
              </button>
            </form>
          </div>
        )}
      </div>

      {/* 10. FOOTER */}
      <footer className="bg-white border-t border-slate-200 text-slate-500 text-xs mt-auto">
        <div className="max-w-7xl mx-auto px-4 py-8 border-b border-slate-100 grid grid-cols-2 md:grid-cols-4 gap-6">
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-2xl bg-indigo-50 text-indigo-600"><ShieldCheck className="w-5 h-5" /></div>
            <div>
              <h4 className="font-bold text-slate-900 text-xs">100% Chính Hãng</h4>
              <p className="text-[11px] text-slate-500">Bảo hành 36 tháng 1 đổi 1</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-2xl bg-cyan-50 text-cyan-600"><Truck className="w-5 h-5" /></div>
            <div>
              <h4 className="font-bold text-slate-900 text-xs">Giao Hàng Miễn Phí</h4>
              <p className="text-[11px] text-slate-500">Đơn hàng PC ráp bộ toàn quốc</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-2xl bg-emerald-50 text-emerald-600"><RotateCcw className="w-5 h-5" /></div>
            <div>
              <h4 className="font-bold text-slate-900 text-xs">Đổi Mới 7 Ngày</h4>
              <p className="text-[11px] text-slate-500">Nếu lỗi kỹ thuật từ NSX</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <div className="p-2.5 rounded-2xl bg-indigo-50 text-indigo-600"><Headphones className="w-5 h-5" /></div>
            <div>
              <h4 className="font-bold text-slate-900 text-xs">Hỗ Trợ AI 24/7</h4>
              <p className="text-[11px] text-slate-500">Tự động đối chiếu tương thích</p>
            </div>
          </div>
        </div>

        <div className="max-w-7xl mx-auto px-4 py-6 flex flex-col sm:flex-row items-center justify-between gap-4 text-[11px]">
          <span className="text-slate-500">© 2026 PCSTORE Vietnam. Hệ thống phân phối linh kiện & máy tính chính hãng.</span>
          <span className="text-slate-500">Đà Nẵng, Việt Nam • Hotline: 1900 6868</span>
        </div>
      </footer>

    </div>
  );
}