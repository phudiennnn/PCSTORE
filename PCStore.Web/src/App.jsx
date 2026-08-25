import React, { useState, useEffect, useCallback } from 'react';
import { 
  Cpu, ShoppingCart, Wrench, Search, ShieldCheck, Truck, 
  RotateCcw, Sparkles, ChevronRight, Layers, 
  Monitor, Zap, Trash2, Plus, AlertCircle, CheckCircle2, Bot, Send, X,
  Headphones, HardDrive, Fan, Box, UserPlus, LogIn, LogOut, Loader2, Mail, Lock, User, Phone, MapPin, AlertTriangle, RefreshCw, Settings, KeyRound, SlidersHorizontal, ArrowUpDown, Eye, EyeOff, Heart, Star, Compass, PhoneCall, ChevronLeft, ArrowLeftRight, Check
} from 'lucide-react';
import { productService, authService } from './services/api';

export default function App() {
  const [activeTab, setActiveTab] = useState('home'); // 'home' | 'builder'
  const [categories, setCategories] = useState([]);
  const [products, setProducts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [apiError, setApiError] = useState(null);

  // States Tìm kiếm & Lọc
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedCategoryTab, setSelectedCategoryTab] = useState('ALL');
  const [priceRange, setPriceRange] = useState('ALL');
  const [sortBy, setSortBy] = useState('default');

  // State Xem chi tiết sản phẩm (Modal Chi Tiết)
  const [selectedProduct, setSelectedProduct] = useState(null);
  const [isDetailOpen, setIsDetailOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailTab, setDetailTab] = useState('specs'); // 'specs' | 'desc'

  // State So Sánh Linh Kiện (Comparison Use Case)
  const [compareList, setCompareList] = useState([]); // Mảng chứa các object product cần so sánh (tối đa 4)
  const [isCompareModalOpen, setIsCompareModalOpen] = useState(false);

  // Auth State & Modal
  const [isAuthOpen, setIsAuthOpen] = useState(false);
  const [authTab, setAuthTab] = useState('signin'); // 'signin' | 'signup'
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(true);

  const [isProfileOpen, setIsProfileOpen] = useState(false);
  const [currentUser, setCurrentUser] = useState(() => {
    try {
      const saved = localStorage.getItem('pcstore_user');
      return saved ? JSON.parse(saved) : null;
    } catch {
      return null;
    }
  });

  // Auth Form State
  const [authForm, setAuthForm] = useState({
    fullName: '',
    email: '',
    password: '',
    confirmPassword: '',
    phoneNumber: '',
    address: ''
  });
  const [authLoading, setAuthLoading] = useState(false);
  const [authError, setAuthError] = useState('');
  const [authSuccess, setAuthSuccess] = useState('');

  // Profile form state
  const [profileForm, setProfileForm] = useState({ fullName: '', phoneNumber: '', address: '', currentPassword: '', newPassword: '' });
  const [profileLoading, setProfileLoading] = useState(false);
  const [profileError, setProfileError] = useState('');
  const [profileSuccess, setProfileSuccess] = useState('');

  // Builder state
  const [selectedParts, setSelectedParts] = useState({});
  const [modalCategory, setModalCategory] = useState(null);

  // AI Chat state (BUILD PC AI)
  const [isChatOpen, setIsChatOpen] = useState(false);
  const [messages, setMessages] = useState([
    { sender: 'bot', text: 'Xin chào! Tôi là Trợ lý AI BUILD PC của PC STORE. Bạn cần tư vấn dàn máy nào?' }
  ]);
  const [chatInput, setChatInput] = useState('');

  // Tải sản phẩm từ API
  const fetchProducts = useCallback(async () => {
    try {
      setLoading(true);
      setApiError(null);

      let minPrice = undefined;
      let maxPrice = undefined;

      if (priceRange === 'UNDER_5M') maxPrice = 5000000;
      else if (priceRange === '5M_15M') { minPrice = 5000000; maxPrice = 15000000; }
      else if (priceRange === 'OVER_15M') minPrice = 15000000;

      const prodData = await productService.getProducts({
        searchTerm: searchTerm.trim(),
        categoryType: selectedCategoryTab,
        minPrice,
        maxPrice,
        sortBy
      });

      setProducts(Array.isArray(prodData) ? prodData : []);
    } catch (err) {
      setApiError('Không thể kết nối đến Backend PostgreSQL (http://localhost:5170).');
    } finally {
      setLoading(false);
    }
  }, [searchTerm, selectedCategoryTab, priceRange, sortBy]);

  useEffect(() => {
    const fetchInit = async () => {
      try {
        const catData = await productService.getCategories();
        setCategories(Array.isArray(catData) ? catData : []);
      } catch (err) {
        console.error('Lỗi tải danh mục:', err);
      }
    };
    fetchInit();
  }, []);

  useEffect(() => {
    fetchProducts();
  }, [fetchProducts]);

  // Logic Toggle So Sánh Sản Phẩm
  const handleToggleCompare = (product) => {
    const exists = compareList.find(p => p.id === product.id);
    if (exists) {
      setCompareList(prev => prev.filter(p => p.id !== product.id));
    } else {
      if (compareList.length >= 4) {
        alert('Bạn chỉ có thể so sánh tối đa 4 sản phẩm cùng một lúc!');
        return;
      }
      setCompareList(prev => [...prev, product]);
    }
  };

  // Xem chi tiết sản phẩm
  const handleOpenDetail = async (productId) => {
    try {
      setIsDetailOpen(true);
      setDetailLoading(true);
      setDetailTab('specs');
      const data = await productService.getProductById(productId);
      setSelectedProduct(data);
    } catch (err) {
      console.error(err);
    } finally {
      setDetailLoading(false);
    }
  };

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
      console.warn('Lỗi lấy hồ sơ:', err);
    }
  };

  // Cập nhật hồ sơ
  const handleProfileSubmit = async (e) => {
    e.preventDefault();
    setProfileError('');
    setProfileSuccess('');

    try {
      setProfileLoading(true);
      const res = await authService.updateProfile(currentUser.id, profileForm);
      setProfileSuccess(res.message || 'Cập nhật hồ sơ thành công!');
      setCurrentUser(res.user);
      localStorage.setItem('pcstore_user', JSON.stringify(res.user));

      setTimeout(() => {
        setIsProfileOpen(false);
        setProfileSuccess('');
      }, 1000);
    } catch (err) {
      setProfileError(err.response?.data?.detail || err.response?.data?.message || 'Lỗi cập nhật hồ sơ.');
    } finally {
      setProfileLoading(false);
    }
  };

  // Xử lý Submit Sign In / Sign Up
  const handleAuthSubmit = async (e) => {
    e.preventDefault();
    setAuthError('');
    setAuthSuccess('');

    if (authTab === 'signup') {
      if (authForm.password !== authForm.confirmPassword) {
        setAuthError('Mật khẩu xác nhận không khớp!');
        return;
      }
      if (authForm.password.length < 6) {
        setAuthError('Mật khẩu phải từ 6 ký tự trở lên.');
        return;
      }

      try {
        setAuthLoading(true);
        const res = await authService.register(authForm);
        setAuthSuccess('Đăng ký tài khoản thành công!');
        setCurrentUser(res.user);
        if (rememberMe) localStorage.setItem('pcstore_user', JSON.stringify(res.user));

        setTimeout(() => {
          setIsAuthOpen(false);
          setAuthSuccess('');
          setAuthForm({ fullName: '', email: '', password: '', confirmPassword: '', phoneNumber: '', address: '' });
        }, 800);
      } catch (err) {
        setAuthError(err.response?.data?.message || 'Lỗi khi đăng ký tài khoản.');
      } finally {
        setAuthLoading(false);
      }
    } else {
      try {
        setAuthLoading(true);
        const res = await authService.login({ email: authForm.email, password: authForm.password });
        setAuthSuccess('Đăng nhập thành công!');
        setCurrentUser(res.user);
        if (rememberMe) localStorage.setItem('pcstore_user', JSON.stringify(res.user));

        setTimeout(() => {
          setIsAuthOpen(false);
          setAuthSuccess('');
          setAuthForm({ fullName: '', email: '', password: '', confirmPassword: '', phoneNumber: '', address: '' });
        }, 600);
      } catch (err) {
        setAuthError(err.response?.data?.message || 'Email hoặc mật khẩu không chính xác!');
      } finally {
        setAuthLoading(false);
      }
    }
  };

  // Đăng xuất (Log Out)
  const handleLogout = async () => {
    try {
      await authService.logout();
    } catch (err) {
      console.warn('Lỗi logout:', err);
    } finally {
      localStorage.removeItem('pcstore_user');
      setCurrentUser(null);
      setIsProfileOpen(false);
    }
  };

  // Ráp PC & Tương thích
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
    compatibilityErrors.push(`Socket không khớp: CPU (${cpu.socket}) không tương thích với Mainboard (${mb.socket})!`);
  }
  if (mb && ram && mb.ramType && ram.ramType && !mb.ramType.toLowerCase().includes(ram.ramType.toLowerCase())) {
    compatibilityErrors.push(`Chuẩn RAM không khớp: Mainboard hỗ trợ (${mb.ramType}) nhưng RAM chọn là (${ram.ramType})!`);
  }
  if (psuWattage > 0 && estimatedTdp > psuWattage) {
    compatibilityErrors.push(`Công suất nguồn yếu: Nguồn (${psuWattage}W) không đáp ứng ước tính (${estimatedTdp}W)!`);
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
        text: `Với yêu cầu "${userMsg}", combo PC STORE tối ưu đề xuất: CPU Intel Core i7 14700K + ASUS B760M + VGA RTX 4070 Super + 32GB RAM DDR5 + Nguồn 750W Gold.` 
      }]);
    }, 600);
  };

  const renderIcon = (type) => {
    switch (type) {
      case 'CPU': return <Cpu className="w-6 h-6 text-cyan-400" />;
      case 'Mainboard': return <Layers className="w-6 h-6 text-cyan-400" />;
      case 'RAM': return <HardDrive className="w-6 h-6 text-cyan-400" />;
      case 'GPU': return <Monitor className="w-6 h-6 text-cyan-400" />;
      case 'SSD': return <HardDrive className="w-6 h-6 text-cyan-400" />;
      case 'PSU': return <Zap className="w-6 h-6 text-cyan-400" />;
      case 'Case': return <Box className="w-6 h-6 text-cyan-400" />;
      case 'Cooler': return <Fan className="w-6 h-6 text-cyan-400" />;
      default: return <Cpu className="w-6 h-6 text-cyan-400" />;
    }
  };

  return (
    <div className="min-h-screen bg-[#070b14] text-slate-100 flex flex-col font-sans selection:bg-cyan-500 selection:text-slate-950 pb-20">
      
      {/* 1. THANH HEADER PC STORE */}
      <header className="sticky top-0 z-40 bg-[#0c1322]/95 backdrop-blur-md border-b border-cyan-950/80 shadow-lg shadow-black/50">
        <div className="max-w-7xl mx-auto px-4 h-20 flex items-center justify-between gap-4">
          
          {/* Logo PC STORE */}
          <div className="flex items-center gap-3 cursor-pointer select-none shrink-0" onClick={() => setActiveTab('home')}>
            <div className="w-11 h-11 rounded-2xl bg-gradient-to-tr from-cyan-500 to-blue-600 p-0.5 shadow-[0_0_15px_rgba(6,182,212,0.5)]">
              <div className="w-full h-full bg-[#0c1322] rounded-[14px] flex items-center justify-center">
                <Cpu className="w-6 h-6 text-cyan-400" />
              </div>
            </div>
            <div className="leading-tight">
              <div className="text-2xl font-black tracking-wider text-white">
                PC <span className="text-cyan-400">STORE</span>
              </div>
              <div className="text-[9px] tracking-widest text-cyan-500 uppercase font-black">
                Smart Hardware Engine
              </div>
            </div>
          </div>

          <div className="hidden lg:flex items-center justify-center w-10 h-10 rounded-full border border-cyan-900/60 bg-[#111a2e] hover:border-cyan-400 cursor-pointer transition shrink-0" title="Chi nhánh: Đà Nẵng">
            <MapPin className="w-5 h-5 text-cyan-400" />
          </div>

          {/* Ô tìm kiếm */}
          <div className="flex-1 max-w-xl relative">
            <input
              type="text"
              placeholder="Bạn cần tìm gì? (Tên linh kiện, CPU, VGA, RAM...)"
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full bg-[#111a2e] border border-cyan-950 focus:border-cyan-400 rounded-full pl-6 pr-12 py-3 text-xs md:text-sm font-medium focus:outline-none text-slate-100 placeholder-slate-500 transition shadow-inner focus:shadow-[0_0_15px_rgba(6,182,212,0.3)]"
            />
            {searchTerm && (
              <button onClick={() => setSearchTerm('')} className="absolute right-11 top-3.5 text-slate-400 hover:text-white">
                <X className="w-4 h-4" />
              </button>
            )}
            <button 
              onClick={() => {
                setActiveTab('home');
                document.getElementById('catalog-section')?.scrollIntoView({ behavior: 'smooth' });
              }}
              className="absolute right-4 top-3.5 text-slate-400 hover:text-cyan-400 transition"
            >
              <Search className="w-5 h-5" />
            </button>
          </div>

          {/* CỤM NÚT ĐIỀU HƯỚNG */}
          <div className="flex items-center gap-1 sm:gap-2 shrink-0 select-none text-slate-200">
            
            <div 
              onClick={() => setActiveTab('builder')}
              className={`flex flex-col items-center justify-center px-2.5 py-1 rounded-xl cursor-pointer transition group ${
                activeTab === 'builder' ? 'bg-cyan-950/80 text-cyan-400 border border-cyan-800/60' : 'hover:bg-cyan-950/40 text-slate-300'
              }`}
            >
              <Box className="w-5 h-5 text-cyan-400 group-hover:scale-110 transition" />
              <span className="text-[11px] font-bold mt-1 text-center whitespace-nowrap leading-tight">
                Xây Dựng Cấu Hình
              </span>
            </div>

            <div 
              onClick={() => alert('Hotline hỗ trợ kỹ thuật PC STORE: 1900 6868 (8:00 - 21:30)')}
              className="flex flex-col items-center justify-center px-2.5 py-1 rounded-xl cursor-pointer hover:bg-cyan-950/40 transition group text-slate-300"
            >
              <PhoneCall className="w-5 h-5 text-cyan-400 group-hover:scale-110 transition" />
              <span className="text-[11px] font-bold mt-1 text-center whitespace-nowrap leading-tight">
                Khách Hàng Liên Hệ
              </span>
            </div>

            <div 
              onClick={() => setIsChatOpen(true)}
              className="flex flex-col items-center justify-center px-3 py-1 rounded-xl cursor-pointer hover:bg-cyan-900/40 bg-cyan-950/70 transition group border border-cyan-500/50 relative shadow-[0_0_12px_rgba(6,182,212,0.25)]"
              title="Mở trợ lý AI BUILD PC"
            >
              <Sparkles className="w-5 h-5 text-cyan-300 animate-pulse group-hover:scale-110 transition" />
              <span className="text-[11px] font-black mt-1 text-center whitespace-nowrap leading-tight text-cyan-300">
                BUILD PC AI
              </span>
            </div>

            <div 
              onClick={() => setActiveTab('builder')}
              className="flex flex-col items-center justify-center px-2.5 py-1 rounded-xl cursor-pointer hover:bg-cyan-950/40 transition group text-slate-300 relative"
            >
              <div className="relative">
                <ShoppingCart className="w-5 h-5 text-cyan-400 group-hover:scale-110 transition" />
                {Object.keys(selectedParts).length > 0 && (
                  <span className="absolute -top-1 -right-2 bg-cyan-400 text-slate-950 rounded-full w-4 h-4 flex items-center justify-center text-[10px] font-black shadow-[0_0_8px_rgba(6,182,212,0.8)]">
                    {Object.keys(selectedParts).length}
                  </span>
                )}
              </div>
              <span className="text-[11px] font-bold mt-1 text-center whitespace-nowrap leading-tight">
                Giỏ Hàng
              </span>
            </div>

            {currentUser ? (
              <div className="flex items-center gap-1.5 pl-1.5 border-l border-cyan-950">
                <div 
                  onClick={handleOpenProfile}
                  className="flex flex-col items-center justify-center px-2 py-1 rounded-xl cursor-pointer hover:bg-cyan-950/40 transition group"
                  title="Xem và sửa hồ sơ cá nhân"
                >
                  <div className="w-5 h-5 rounded-full bg-cyan-400 text-slate-950 flex items-center justify-center text-[10px] font-black">
                    {currentUser.fullName ? currentUser.fullName.charAt(0).toUpperCase() : 'U'}
                  </div>
                  <span className="text-[11px] font-bold mt-1 text-center max-w-[65px] truncate leading-tight text-cyan-300">
                    {currentUser.fullName}
                  </span>
                </div>

                <button
                  onClick={handleLogout}
                  className="p-2 rounded-xl bg-rose-950/40 hover:bg-rose-900/60 text-rose-400 border border-rose-800/40 hover:border-rose-500 transition shadow-xs flex items-center justify-center"
                  title="Đăng xuất"
                >
                  <LogOut className="w-4 h-4" />
                </button>
              </div>
            ) : (
              <div 
                onClick={() => { setAuthTab('signin'); setIsAuthOpen(true); }}
                className="flex flex-col items-center justify-center px-2.5 py-1 rounded-xl cursor-pointer hover:bg-cyan-950/40 transition group text-slate-300"
              >
                <User className="w-5 h-5 text-cyan-400 group-hover:scale-110 transition" />
                <span className="text-[11px] font-bold mt-1 text-center whitespace-nowrap leading-tight">
                  Tài Khoản
                </span>
              </div>
            )}

          </div>

        </div>
      </header>

      {/* 2. MAIN CONTENT */}
      <main className="flex-1 max-w-7xl mx-auto px-4 w-full py-8">
        
        {apiError ? (
          <div className="bg-rose-950/30 border border-rose-800/60 rounded-3xl p-8 text-center space-y-4 max-w-xl mx-auto my-12">
            <AlertTriangle className="w-8 h-8 text-rose-500 mx-auto" />
            <p className="text-xs text-rose-300">{apiError}</p>
            <button onClick={fetchProducts} className="px-4 py-2 bg-cyan-500 text-slate-950 font-bold text-xs rounded-xl shadow-[0_0_15px_rgba(6,182,212,0.4)]">Thử lại</button>
          </div>
        ) : activeTab === 'home' ? (
          <div className="space-y-12">
            
            {/* HERO BANNER */}
            <div className="relative overflow-hidden rounded-3xl bg-gradient-to-r from-[#0c1424] via-[#0e1b33] to-[#0a1120] border border-cyan-900/40 p-8 md:p-14 shadow-[0_0_30px_rgba(0,0,0,0.8)]">
              <div className="absolute top-0 right-1/4 w-96 h-96 bg-cyan-500/10 rounded-full blur-3xl pointer-events-none"></div>
              
              <div className="grid grid-cols-1 lg:grid-cols-12 gap-8 items-center relative z-10">
                <div className="lg:col-span-7 space-y-6">
                  <h1 className="text-4xl md:text-5xl font-black text-white tracking-tight leading-none uppercase">
                    Ultimate Power. <br />
                    <span className="text-transparent bg-clip-text bg-gradient-to-r from-cyan-400 via-teal-300 to-blue-500">
                      Unleashed.
                    </span>
                  </h1>
                  
                  <p className="text-xs md:text-sm text-slate-400 max-w-md leading-relaxed">
                    Trải nghiệm hệ thống phần cứng đỉnh cao được đồng bộ thời gian thực từ cơ sở dữ liệu PostgreSQL.
                  </p>

                  <div className="flex items-center gap-4 pt-2">
                    <button 
                      onClick={() => document.getElementById('catalog-section')?.scrollIntoView({ behavior: 'smooth' })}
                      className="px-6 py-3 bg-cyan-400 hover:bg-cyan-300 text-slate-950 font-black text-xs rounded-xl shadow-[0_0_20px_rgba(6,182,212,0.4)] transition uppercase tracking-wider"
                    >
                      Shop Now
                    </button>
                    <button 
                      onClick={() => setActiveTab('builder')}
                      className="px-6 py-3 bg-transparent border border-cyan-500/50 hover:border-cyan-400 text-cyan-300 hover:text-white font-black text-xs rounded-xl transition uppercase tracking-wider"
                    >
                      Custom Build
                    </button>
                  </div>
                </div>

                <div className="lg:col-span-5 flex items-center justify-center">
                  <div className="relative w-full max-w-sm h-64 rounded-2xl bg-[#111b30] border border-cyan-900/60 p-6 flex flex-col items-center justify-center shadow-[0_0_25px_rgba(6,182,212,0.2)]">
                    <Monitor className="w-24 h-24 text-cyan-400 mb-3 animate-pulse" />
                    <span className="text-sm font-black text-white tracking-wide">NVIDIA GeForce RTX 40 Series</span>
                    <span className="text-[10px] text-cyan-400 font-mono mt-1">DLSS 3.0 • 16GB GDDR6X • Ray Tracing</span>
                  </div>
                </div>
              </div>
            </div>

            {/* CATEGORY NAVIGATION */}
            <div className="space-y-4">
              <h3 className="text-xs font-black tracking-widest text-slate-400 uppercase">Category Navigation</h3>
              
              <div className="grid grid-cols-4 sm:grid-cols-8 gap-3">
                <div
                  onClick={() => setSelectedCategoryTab('ALL')}
                  className={`flex flex-col items-center justify-center p-3.5 rounded-2xl border cursor-pointer transition ${
                    selectedCategoryTab === 'ALL'
                      ? 'bg-cyan-950/60 border-cyan-400 shadow-[0_0_15px_rgba(6,182,212,0.4)]'
                      : 'bg-[#0d1627] border-cyan-950/60 hover:border-cyan-500/40 text-slate-400 hover:text-white'
                  }`}
                >
                  <Compass className="w-6 h-6 text-cyan-400 mb-1.5" />
                  <span className="text-[11px] font-bold">All</span>
                </div>

                {categories.map((c) => (
                  <div
                    key={c.id}
                    onClick={() => {
                      setSelectedCategoryTab(c.type);
                      document.getElementById('catalog-section')?.scrollIntoView({ behavior: 'smooth' });
                    }}
                    className={`flex flex-col items-center justify-center p-3.5 rounded-2xl border cursor-pointer transition ${
                      selectedCategoryTab === c.type
                        ? 'bg-cyan-950/60 border-cyan-400 shadow-[0_0_15px_rgba(6,182,212,0.4)]'
                        : 'bg-[#0d1627] border-cyan-950/60 hover:border-cyan-500/40 text-slate-400 hover:text-white'
                    }`}
                  >
                    <div className="mb-1.5">{renderIcon(c.type)}</div>
                    <span className="text-[11px] font-bold truncate max-w-full">{c.name}</span>
                  </div>
                ))}
              </div>
            </div>

            {/* KHO LINH KIỆN & BỘ LỌC */}
            <section id="catalog-section" className="space-y-6">
              
              <div className="flex flex-col sm:flex-row justify-between items-start sm:items-center gap-4 border-b border-cyan-950/60 pb-4">
                <div>
                  <h2 className="text-xl font-black text-white tracking-tight uppercase">Featured Components</h2>
                  <p className="text-xs text-slate-500">Live PostgreSQL Data ({products.length} Products)</p>
                </div>

                <div className="flex items-center gap-3">
                  <div className="flex items-center gap-1.5 bg-[#0d1627] border border-cyan-950 px-3 py-1.5 rounded-xl text-xs">
                    <SlidersHorizontal className="w-3.5 h-3.5 text-cyan-400" />
                    <select
                      value={priceRange}
                      onChange={(e) => setPriceRange(e.target.value)}
                      className="bg-transparent text-slate-300 font-bold focus:outline-none cursor-pointer"
                    >
                      <option value="ALL" className="bg-[#0c1322]">All Prices</option>
                      <option value="UNDER_5M" className="bg-[#0c1322]">&lt; 5.000.000 đ</option>
                      <option value="5M_15M" className="bg-[#0c1322]">5.000.000 - 15.000.000 đ</option>
                      <option value="OVER_15M" className="bg-[#0c1322]">&gt; 15.000.000 đ</option>
                    </select>
                  </div>

                  <div className="flex items-center gap-1.5 bg-[#0d1627] border border-cyan-950 px-3 py-1.5 rounded-xl text-xs">
                    <ArrowUpDown className="w-3.5 h-3.5 text-cyan-400" />
                    <select
                      value={sortBy}
                      onChange={(e) => setSortBy(e.target.value)}
                      className="bg-transparent text-slate-300 font-bold focus:outline-none cursor-pointer"
                    >
                      <option value="default" className="bg-[#0c1322]">Default Sort</option>
                      <option value="price_asc" className="bg-[#0c1322]">Price: Low to High</option>
                      <option value="price_desc" className="bg-[#0c1322]">Price: High to Low</option>
                    </select>
                  </div>
                </div>
              </div>

              {loading ? (
                <div className="py-20 text-center space-y-3">
                  <Loader2 className="w-8 h-8 text-cyan-400 animate-spin mx-auto" />
                  <p className="text-xs text-slate-500">Querying live components from PostgreSQL...</p>
                </div>
              ) : products.length === 0 ? (
                <div className="bg-[#0c1424] border border-cyan-950 rounded-3xl p-12 text-center text-slate-500">
                  Không tìm thấy linh kiện phù hợp với từ khóa và bộ lọc.
                </div>
              ) : (
                <div className="grid grid-cols-1 sm:grid-cols-2 md:grid-cols-3 lg:grid-cols-4 gap-6">
                  {products.map((p) => {
                    const isCompared = compareList.some(item => item.id === p.id);
                    return (
                      <div 
                        key={p.id} 
                        className={`bg-[#0c1424] border rounded-3xl p-5 flex flex-col justify-between space-y-4 transition shadow-lg group relative ${
                          isCompared ? 'border-cyan-400 shadow-[0_0_20px_rgba(6,182,212,0.3)]' : 'border-cyan-950/80 hover:border-cyan-500/60'
                        }`}
                      >
                        {/* NÚT CHỌN SO SÁNH GÓC PHẢI */}
                        <button
                          onClick={() => handleToggleCompare(p)}
                          className={`absolute top-3 right-3 z-10 px-2.5 py-1 rounded-lg text-[10px] font-bold flex items-center gap-1 transition ${
                            isCompared 
                              ? 'bg-cyan-400 text-slate-950 shadow-[0_0_10px_rgba(6,182,212,0.6)]' 
                              : 'bg-[#111a2e]/90 text-slate-400 hover:text-cyan-300 border border-cyan-950'
                          }`}
                          title="Thêm vào danh sách so sánh"
                        >
                          <ArrowLeftRight className="w-3 h-3" />
                          <span>{isCompared ? 'Đã chọn' : 'So sánh'}</span>
                        </button>

                        <div className="space-y-3">
                          <div 
                            onClick={() => handleOpenDetail(p.id)}
                            className="w-full h-44 bg-[#111a2e] rounded-2xl flex flex-col items-center justify-center p-4 relative border border-cyan-950/40 cursor-pointer overflow-hidden"
                          >
                            <span className="absolute top-2.5 left-2.5 text-[9px] font-black uppercase text-cyan-300 bg-cyan-950/80 border border-cyan-800 px-2 py-0.5 rounded-md">
                              {p.brand}
                            </span>
                            
                            {p.imageUrl && p.imageUrl.startsWith('http') ? (
                              <img src={p.imageUrl} alt={p.name} className="h-28 object-contain group-hover:scale-105 transition" />
                            ) : (
                              <div className="p-4 bg-[#090e1a] rounded-2xl border border-cyan-950 group-hover:scale-105 transition">
                                {renderIcon(p.categoryType)}
                              </div>
                            )}

                            <span className="absolute bottom-2.5 right-2.5 text-[9px] font-bold text-slate-500 uppercase">
                              {p.categoryType}
                            </span>
                          </div>

                          <div className="flex items-center gap-1 text-amber-400">
                            {[...Array(5)].map((_, i) => (
                              <Star key={i} className="w-3 h-3 fill-amber-400" />
                            ))}
                            <span className="text-[10px] text-slate-500 ml-1">5.0</span>
                          </div>

                          <h4 
                            onClick={() => handleOpenDetail(p.id)}
                            className="text-xs font-bold text-white line-clamp-2 leading-snug cursor-pointer group-hover:text-cyan-400 transition"
                          >
                            {p.name}
                          </h4>

                          <div className="flex gap-1 flex-wrap text-[9px]">
                            {p.socket && <span className="bg-cyan-950/60 text-cyan-300 border border-cyan-800/60 px-2 py-0.5 rounded font-mono">Socket {p.socket}</span>}
                            {p.ramType && <span className="bg-cyan-950/60 text-cyan-300 border border-cyan-800/60 px-2 py-0.5 rounded font-mono">{p.ramType}</span>}
                            {p.tdpWattage > 0 && <span className="bg-cyan-950/60 text-cyan-300 border border-cyan-800/60 px-2 py-0.5 rounded font-mono">{p.tdpWattage}W TDP</span>}
                          </div>
                        </div>

                        <div className="space-y-3 pt-2 border-t border-cyan-950/40">
                          <div className="text-lg font-black text-cyan-400">
                            {p.price?.toLocaleString('vi-VN')} đ
                          </div>

                          <div className="grid grid-cols-2 gap-2">
                            <button
                              onClick={() => {
                                handleSelectPart(p);
                                setActiveTab('builder');
                              }}
                              className="py-2 bg-cyan-400 hover:bg-cyan-300 text-slate-950 font-black text-[10px] rounded-xl transition uppercase tracking-wider shadow-[0_0_10px_rgba(6,182,212,0.3)]"
                            >
                              Ráp PC
                            </button>
                            
                            <button
                              onClick={() => handleOpenDetail(p.id)}
                              className="py-2 bg-transparent hover:bg-cyan-950/40 border border-cyan-500/40 text-cyan-300 font-bold text-[10px] rounded-xl transition uppercase tracking-wider"
                            >
                              Chi Tiết
                            </button>
                          </div>
                        </div>
                      </div>
                    );
                  })}
                </div>
              )}
            </section>

          </div>
        ) : (
          /* TRANG BUILDER */
          <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
            <div className="lg:col-span-2 space-y-4">
              <div className="flex items-center justify-between pb-3 border-b border-cyan-950/60">
                <div>
                  <h2 className="text-lg font-black text-white uppercase tracking-wider">Custom PC Builder</h2>
                  <p className="text-xs text-slate-500">Live Hardware Compatibility & TDP Calculator</p>
                </div>
                <button 
                  onClick={() => setSelectedParts({})}
                  className="text-xs text-rose-400 hover:underline font-bold"
                >
                  Clear All Parts
                </button>
              </div>

              {categories.map((cat) => {
                const part = selectedParts[cat.type];
                return (
                  <div key={cat.id} className="bg-[#0c1424] border border-cyan-950/80 rounded-2xl p-4 flex items-center justify-between gap-4 shadow-md">
                    <div className="flex items-center gap-3.5 min-w-0">
                      <div className="w-12 h-12 rounded-xl bg-[#111a2e] border border-cyan-950 flex items-center justify-center shrink-0">
                        {renderIcon(cat.type)}
                      </div>
                      <div className="truncate">
                        <span className="text-[10px] font-black uppercase text-cyan-400 tracking-wider">{cat.name}</span>
                        {part ? (
                          <p className="text-xs font-bold text-white truncate">{part.name}</p>
                        ) : (
                          <p className="text-xs text-slate-600 italic">No component selected...</p>
                        )}
                      </div>
                    </div>

                    <div className="flex items-center gap-3 shrink-0">
                      {part ? (
                        <>
                          <span className="text-xs font-black text-cyan-400">{part.price?.toLocaleString('vi-VN')} đ</span>
                          <button onClick={() => handleRemovePart(cat.type)} className="p-2 hover:bg-rose-950/40 text-rose-400 rounded-xl transition">
                            <Trash2 className="w-4 h-4" />
                          </button>
                        </>
                      ) : (
                        <button 
                          onClick={() => setModalCategory(cat)}
                          className="px-4 py-2 rounded-xl bg-cyan-950/60 border border-cyan-500/40 hover:border-cyan-400 text-cyan-300 text-xs font-bold transition"
                        >
                          + Select
                        </button>
                      )}
                    </div>
                  </div>
                );
              })}
            </div>

            {/* BẢNG ĐỐI CHIẾU TƯƠNG THÍCH */}
            <div className="space-y-6">
              <div className="bg-[#0c1424] border border-cyan-950/80 rounded-3xl p-6 sticky top-36 space-y-6 shadow-xl">
                <h3 className="text-xs font-black text-white uppercase tracking-widest border-b border-cyan-950/60 pb-3">Compatibility Status</h3>

                <div className="space-y-2">
                  <div className="flex justify-between text-xs font-bold">
                    <span className="text-cyan-400 flex items-center gap-1"><Zap className="w-4 h-4" /> Total Estimated TDP</span>
                    <span className="text-white">{estimatedTdp}W</span>
                  </div>
                  <div className="w-full bg-[#111a2e] h-2.5 rounded-full overflow-hidden border border-cyan-950">
                    <div 
                      className={`h-full transition-all ${estimatedTdp > (psuWattage || 500) ? 'bg-rose-500 shadow-[0_0_10px_rgba(244,63,94,0.6)]' : 'bg-cyan-400 shadow-[0_0_10px_rgba(6,182,212,0.6)]'}`} 
                      style={{ width: `${Math.min((estimatedTdp / (psuWattage || 650)) * 100, 100)}%` }}
                    />
                  </div>
                  {psuWattage > 0 && <p className="text-[10px] text-slate-500">Selected PSU: <strong className="text-cyan-300">{psuWattage}W</strong></p>}
                </div>

                <div className="space-y-2">
                  {compatibilityErrors.length > 0 ? (
                    compatibilityErrors.map((err, idx) => (
                      <div key={idx} className="flex gap-2 p-3 bg-rose-950/40 border border-rose-800/60 rounded-2xl text-rose-300 text-xs items-start">
                        <AlertCircle className="w-4 h-4 shrink-0 mt-0.5 text-rose-500" />
                        <span>{err}</span>
                      </div>
                    ))
                  ) : (
                    <div className="flex gap-2 p-3 bg-emerald-950/40 border border-emerald-800/60 rounded-2xl text-emerald-300 text-xs items-center">
                      <CheckCircle2 className="w-4 h-4 shrink-0 text-emerald-400" />
                      <span>All selected components are fully compatible!</span>
                    </div>
                  )}
                </div>

                <div className="border-t border-cyan-950/60 pt-4 space-y-4">
                  <div className="flex justify-between items-baseline">
                    <span className="text-xs text-slate-500 uppercase font-bold">Total Price:</span>
                    <span className="text-xl font-black text-cyan-400">{totalPrice.toLocaleString('vi-VN')} đ</span>
                  </div>
                  
                  <button 
                    disabled={compatibilityErrors.length > 0 || totalPrice === 0}
                    className="w-full py-3.5 rounded-xl bg-cyan-400 hover:bg-cyan-300 disabled:opacity-40 text-slate-950 font-black text-xs shadow-[0_0_20px_rgba(6,182,212,0.4)] transition uppercase tracking-wider"
                  >
                    Proceed to Order
                  </button>
                </div>
              </div>
            </div>
          </div>
        )}
      </main>

      {/* 3. THANH GHIM SO SÁNH CỐ ĐỊNH PHÍA DƯỚI (FLOATING COMPARE TRAY) */}
      {compareList.length > 0 && (
        <div className="fixed bottom-0 left-0 right-0 z-40 bg-[#0c1424]/95 border-t border-cyan-500/50 backdrop-blur-md p-3.5 shadow-[0_-10px_30px_rgba(0,0,0,0.8)] animate-in slide-in-from-bottom duration-300">
          <div className="max-w-7xl mx-auto flex flex-col sm:flex-row items-center justify-between gap-4">
            
            <div className="flex items-center gap-3 overflow-x-auto max-w-full">
              <div className="flex items-center gap-2 pr-3 border-r border-cyan-950 shrink-0">
                <ArrowLeftRight className="w-5 h-5 text-cyan-400" />
                <div>
                  <span className="text-xs font-black text-white block uppercase tracking-wider">So Sánh ({compareList.length}/4)</span>
                  <span className="text-[10px] text-slate-400">Chọn linh kiện để đối chiếu</span>
                </div>
              </div>

              {/* Danh sách các linh kiện đã chọn */}
              <div className="flex items-center gap-2">
                {compareList.map(item => (
                  <div key={item.id} className="flex items-center gap-2 bg-[#111a2e] border border-cyan-950 rounded-xl px-3 py-1.5 shrink-0 relative group">
                    <span className="text-xs font-bold text-slate-200 max-w-[130px] truncate">{item.name}</span>
                    <button 
                      onClick={() => handleToggleCompare(item)} 
                      className="text-slate-500 hover:text-rose-400"
                      title="Xóa khỏi so sánh"
                    >
                      <X className="w-3.5 h-3.5" />
                    </button>
                  </div>
                ))}
              </div>
            </div>

            <div className="flex items-center gap-2 shrink-0">
              <button
                onClick={() => setCompareList([])}
                className="px-3.5 py-2 rounded-xl text-xs font-bold text-slate-400 hover:text-rose-400 transition"
              >
                Xóa tất cả
              </button>
              
              <button
                disabled={compareList.length < 2}
                onClick={() => setIsCompareModalOpen(true)}
                className="px-6 py-2 bg-cyan-400 hover:bg-cyan-300 disabled:opacity-40 text-slate-950 font-black text-xs rounded-xl shadow-[0_0_15px_rgba(6,182,212,0.4)] transition uppercase tracking-wider flex items-center gap-2"
              >
                <ArrowLeftRight className="w-4 h-4" />
                <span>So Sánh Ngay</span>
              </button>
            </div>

          </div>
        </div>
      )}

      {/* 4. MODAL BẢNG SO SÁNH CHI TIẾT ĐA CHIỀU (SIDE-BY-SIDE MATRIX) */}
      {isCompareModalOpen && (
        <div className="fixed inset-0 z-50 bg-black/90 backdrop-blur-md flex items-center justify-center p-2 sm:p-4 overflow-y-auto animate-in zoom-in-95 duration-200">
          <div className="bg-[#0c1424] border border-cyan-900 rounded-3xl max-w-6xl w-full max-h-[92vh] flex flex-col shadow-2xl overflow-hidden my-auto text-slate-200">
            
            {/* Header Modal */}
            <div className="p-4 border-b border-cyan-950 flex justify-between items-center bg-[#090e1a]">
              <div className="flex items-center gap-2">
                <ArrowLeftRight className="w-5 h-5 text-cyan-400" />
                <h2 className="text-sm md:text-base font-black text-white uppercase tracking-wider">
                  Bảng So Sánh Thông Số Linh Kiện ({compareList.length} sản phẩm)
                </h2>
              </div>
              <button onClick={() => setIsCompareModalOpen(false)} className="text-slate-500 hover:text-white p-1">
                <X className="w-5 h-5" />
              </button>
            </div>

            {/* Nội dung bảng so sánh song song */}
            <div className="overflow-x-auto p-4 sm:p-6 flex-1">
              <table className="w-full border-collapse text-xs">
                <thead>
                  <tr>
                    <th className="p-3 text-left bg-[#111a2e] text-slate-400 font-bold uppercase w-44 rounded-l-xl">Tiêu chí so sánh</th>
                    {compareList.map(item => (
                      <th key={item.id} className="p-4 text-center bg-[#111a2e] border-l border-cyan-950/60 min-w-[220px]">
                        <div className="flex flex-col items-center space-y-2">
                          <div className="w-20 h-20 bg-[#090e1a] rounded-xl border border-cyan-950 flex items-center justify-center p-2">
                            {item.imageUrl && item.imageUrl.startsWith('http') ? (
                              <img src={item.imageUrl} alt="" className="h-full object-contain" />
                            ) : (
                              renderIcon(item.categoryType)
                            )}
                          </div>
                          <span className="text-[10px] text-cyan-400 font-bold uppercase">{item.brand} • {item.categoryType}</span>
                          <h4 className="font-bold text-white line-clamp-2">{item.name}</h4>
                          <span className="text-sm font-black text-cyan-300">{item.price?.toLocaleString('vi-VN')} đ</span>
                          <button
                            onClick={() => {
                              handleSelectPart(item);
                              setIsCompareModalOpen(false);
                              setActiveTab('builder');
                            }}
                            className="px-4 py-1.5 bg-cyan-400 text-slate-950 font-black text-[10px] rounded-lg hover:bg-cyan-300 transition uppercase"
                          >
                            Chọn Ráp PC
                          </button>
                        </div>
                      </th>
                    ))}
                  </tr>
                </thead>

                <tbody className="divide-y divide-cyan-950/60">
                  <tr className="hover:bg-cyan-950/20">
                    <td className="p-3 font-bold text-slate-400">Giá bán chính hãng</td>
                    {compareList.map(p => (
                      <td key={p.id} className="p-3 text-center text-cyan-400 font-bold border-l border-cyan-950/60">
                        {p.price?.toLocaleString('vi-VN')} đ
                      </td>
                    ))}
                  </tr>

                  <tr className="hover:bg-cyan-950/20">
                    <td className="p-3 font-bold text-slate-400">Thương hiệu / Hãng</td>
                    {compareList.map(p => (
                      <td key={p.id} className="p-3 text-center text-white font-semibold border-l border-cyan-950/60">{p.brand}</td>
                    ))}
                  </tr>

                  <tr className="hover:bg-cyan-950/20">
                    <td className="p-3 font-bold text-slate-400">Chân cắm Socket</td>
                    {compareList.map(p => (
                      <td key={p.id} className="p-3 text-center font-bold text-cyan-300 border-l border-cyan-950/60">
                        {p.specs?.socket || p.socket || '—'}
                      </td>
                    ))}
                  </tr>

                  <tr className="hover:bg-cyan-950/20">
                    <td className="p-3 font-bold text-slate-400">Chuẩn RAM</td>
                    {compareList.map(p => (
                      <td key={p.id} className="p-3 text-center font-bold text-cyan-300 border-l border-cyan-950/60">
                        {p.specs?.ramType || p.ramType || '—'}
                      </td>
                    ))}
                  </tr>

                  <tr className="hover:bg-cyan-950/20">
                    <td className="p-3 font-bold text-slate-400">Tốc độ Bus RAM</td>
                    {compareList.map(p => (
                      <td key={p.id} className="p-3 text-center font-semibold text-slate-200 border-l border-cyan-950/60">
                        {p.specs?.ramBusSpeed ? `${p.specs.ramBusSpeed} MHz` : p.ramBusSpeed ? `${p.ramBusSpeed} MHz` : '—'}
                      </td>
                    ))}
                  </tr>

                  <tr className="hover:bg-cyan-950/20">
                    <td className="p-3 font-bold text-slate-400">Mức ăn điện (TDP)</td>
                    {compareList.map(p => (
                      <td key={p.id} className="p-3 text-center font-bold text-amber-400 border-l border-cyan-950/60">
                        {(p.specs?.tdpWattage || p.tdpWattage) ? `${p.specs?.tdpWattage || p.tdpWattage} W` : '—'}
                      </td>
                    ))}
                  </tr>

                  <tr className="hover:bg-cyan-950/20">
                    <td className="p-3 font-bold text-slate-400">Nguồn đề nghị (PSU)</td>
                    {compareList.map(p => (
                      <td key={p.id} className="p-3 text-center font-bold text-rose-400 border-l border-cyan-950/60">
                        {(p.specs?.recommendedPsu || p.recommendedPsu) ? `≥ ${p.specs?.recommendedPsu || p.recommendedPsu} W` : '—'}
                      </td>
                    ))}
                  </tr>

                  <tr className="hover:bg-cyan-950/20">
                    <td className="p-3 font-bold text-slate-400">Kích thước (Form Factor)</td>
                    {compareList.map(p => (
                      <td key={p.id} className="p-3 text-center font-semibold text-slate-300 border-l border-cyan-950/60">
                        {p.specs?.formFactor || p.formFactor || '—'}
                      </td>
                    ))}
                  </tr>

                  <tr className="hover:bg-cyan-950/20">
                    <td className="p-3 font-bold text-slate-400">Tồn kho hiện tại</td>
                    {compareList.map(p => (
                      <td key={p.id} className="p-3 text-center font-semibold text-emerald-400 border-l border-cyan-950/60">
                        Còn hàng ({p.stockQuantity || 10} chiếc)
                      </td>
                    ))}
                  </tr>
                </tbody>
              </table>
            </div>

            <div className="p-4 border-t border-cyan-950 flex justify-end bg-[#090e1a]">
              <button
                onClick={() => setIsCompareModalOpen(false)}
                className="px-6 py-2.5 bg-cyan-950 border border-cyan-500/40 text-cyan-300 font-bold text-xs rounded-xl hover:bg-cyan-900/40"
              >
                Đóng Bảng So Sánh
              </button>
            </div>

          </div>
        </div>
      )}

      {/* 5. MODAL XEM CHI TIẾT SẢN PHẨM */}
      {isDetailOpen && (
        <div className="fixed inset-0 z-50 bg-black/85 backdrop-blur-sm flex items-center justify-center p-2 sm:p-4 overflow-y-auto animate-in fade-in duration-200">
          <div className="bg-white text-slate-800 rounded-2xl max-w-5xl w-full max-h-[92vh] flex flex-col shadow-2xl overflow-hidden my-auto border border-slate-200">
            
            <div className="p-4 border-b border-slate-200 flex justify-between items-center bg-slate-50">
              <div className="text-xs text-slate-500 flex items-center gap-1.5 font-medium truncate">
                <span>Trang chủ</span>
                <ChevronRight className="w-3.5 h-3.5 text-slate-400" />
                <span>LINH KIỆN MÁY TÍNH</span>
                <ChevronRight className="w-3.5 h-3.5 text-slate-400" />
                <span className="text-blue-600 font-bold truncate">{selectedProduct?.categoryName}</span>
              </div>
              <button 
                onClick={() => setIsDetailOpen(false)} 
                className="p-1.5 rounded-full hover:bg-slate-200 text-slate-400 hover:text-slate-700 transition shrink-0 ml-2"
              >
                <X className="w-5 h-5" />
              </button>
            </div>

            {detailLoading ? (
              <div className="py-24 text-center space-y-3">
                <Loader2 className="w-10 h-10 text-blue-600 animate-spin mx-auto" />
                <p className="text-xs font-bold text-slate-500">Đang tải chi tiết...</p>
              </div>
            ) : selectedProduct && (
              <div className="overflow-y-auto p-4 sm:p-6 space-y-8 flex-1">
                
                <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
                  
                  {/* Cột trái */}
                  <div className="lg:col-span-7 space-y-4">
                    <div className="relative w-full h-72 sm:h-80 bg-white border border-slate-200 rounded-2xl flex items-center justify-center p-6 shadow-xs group">
                      {selectedProduct.imageUrl && selectedProduct.imageUrl.startsWith('http') ? (
                        <img src={selectedProduct.imageUrl} alt={selectedProduct.name} className="max-h-full max-w-full object-contain" />
                      ) : (
                        <div className="p-8 bg-slate-50 rounded-2xl border border-slate-200">
                          {renderIcon(selectedProduct.categoryType)}
                        </div>
                      )}
                    </div>

                    <div className="bg-slate-50 border border-slate-200 rounded-xl p-3.5 space-y-2.5">
                      <div className="flex justify-between items-center">
                        <span className="text-xs font-black uppercase text-slate-800">Thông số sản phẩm</span>
                        <span 
                          onClick={() => {
                            document.getElementById('full-specs-table')?.scrollIntoView({ behavior: 'smooth' });
                          }}
                          className="text-[11px] font-bold text-red-600 hover:underline cursor-pointer border border-red-200 px-2 py-0.5 rounded bg-white"
                        >
                          Xem tất cả thông số
                        </span>
                      </div>

                      <div className="grid grid-cols-3 gap-2 text-xs border-t border-slate-200 pt-2.5">
                        <div>
                          <span className="text-[10px] text-slate-400 block font-semibold">Thương hiệu</span>
                          <strong className="text-blue-600 font-bold">{selectedProduct.brand || 'Chính hãng'}</strong>
                        </div>
                        <div>
                          <span className="text-[10px] text-slate-400 block font-semibold">Model / SKU</span>
                          <strong className="text-blue-600 font-bold truncate block">{selectedProduct.sku || selectedProduct.categoryName}</strong>
                        </div>
                        <div>
                          <span className="text-[10px] text-slate-400 block font-semibold">Chuẩn Socket / RAM</span>
                          <strong className="text-blue-600 font-bold">{selectedProduct.specs?.socket || selectedProduct.specs?.ramType || 'Tiêu chuẩn'}</strong>
                        </div>
                      </div>
                    </div>

                    <div className="space-y-2 border border-slate-200 rounded-xl p-3 text-xs text-slate-600">
                      <span className="text-[11px] font-black uppercase text-slate-800 block">Chính sách mua hàng</span>
                      <div className="grid grid-cols-2 gap-2 text-[11px]">
                        <div className="flex items-center gap-1.5">
                          <ShieldCheck className="w-4 h-4 text-blue-600 shrink-0" />
                          <span>Cam kết giá tốt.</span>
                        </div>
                        <div className="flex items-center gap-1.5">
                          <CheckCircle2 className="w-4 h-4 text-emerald-600 shrink-0" />
                          <span>Sản phẩm mới 100%.</span>
                        </div>
                        <div className="flex items-center gap-1.5">
                          <RotateCcw className="w-4 h-4 text-amber-500 shrink-0" />
                          <span>Lỗi 1 đổi 1 ngay lập tức.</span>
                        </div>
                        <div className="flex items-center gap-1.5">
                          <CreditCard className="w-4 h-4 text-indigo-600 shrink-0" />
                          <span>Hỗ trợ trả góp - Thủ tục nhanh gọn.</span>
                        </div>
                      </div>
                    </div>
                  </div>

                  {/* Cột phải */}
                  <div className="lg:col-span-5 space-y-3.5">
                    <div>
                      <h1 className="text-base sm:text-lg font-black text-slate-900 leading-snug">
                        {selectedProduct.name}
                      </h1>
                      <div className="flex items-center gap-3 text-xs text-slate-500 mt-1 flex-wrap font-medium">
                        <span>Mã SP: <strong className="text-blue-600">{selectedProduct.sku || 'PCSTORE' + selectedProduct.id}</strong></span>
                        <span>•</span>
                        <span>Tình trạng: <strong className="text-emerald-600 font-bold">Còn hàng ({selectedProduct.stockQuantity || 10})</strong></span>
                      </div>
                      <div className="text-xs text-slate-500 mt-0.5 font-semibold">
                        Bảo hành: <strong className="text-red-600">36 tháng 1 đổi 1</strong>
                      </div>
                    </div>

                    <div className="bg-sky-50/50 border border-sky-200 rounded-xl p-3 text-center">
                      <div className="text-2xl sm:text-3xl font-black text-red-600 tracking-tight">
                        {selectedProduct.price?.toLocaleString('vi-VN')}đ
                      </div>
                    </div>

                    <div className="border border-amber-300 rounded-xl overflow-hidden text-xs">
                      <div className="bg-gradient-to-r from-red-600 to-amber-500 text-white font-black px-3.5 py-2 uppercase text-[11px] flex items-center gap-1.5">
                        <Sparkles className="w-3.5 h-3.5 text-amber-200" />
                        <span>Khuyến mại & Quà tặng khác</span>
                      </div>
                      <div className="p-3 bg-amber-50/30 space-y-2 text-[11px] text-slate-700">
                        <div>
                          <strong className="text-red-600 block">* GIÁ ƯU ĐÃI KHI MUA KÈM PC: {((selectedProduct.price || 0) * 0.95).toLocaleString('vi-VN')}đ</strong>
                          <span className="text-slate-500 italic">Áp dụng khi mua trọn bộ linh kiện tại PC STORE.</span>
                        </div>
                      </div>
                    </div>

                    <div className="space-y-2 pt-1">
                      <button 
                        onClick={() => {
                          handleSelectPart(selectedProduct);
                          setIsDetailOpen(false);
                          setActiveTab('builder');
                        }}
                        className="w-full py-3.5 bg-gradient-to-r from-red-600 to-rose-600 hover:from-red-700 hover:to-rose-700 text-white font-black text-sm rounded-xl shadow-md uppercase tracking-wider transition active:scale-[0.99]"
                      >
                        MUA NGAY (RÁP VÀO DÀN PC)
                      </button>

                      <div className="grid grid-cols-2 gap-2">
                        <button 
                          onClick={() => handleToggleCompare(selectedProduct)}
                          className="py-2.5 border border-cyan-500 hover:bg-cyan-50 text-cyan-600 font-bold text-xs rounded-xl flex items-center justify-center gap-1.5 transition"
                        >
                          <ArrowLeftRight className="w-4 h-4" />
                          <span>{compareList.some(i => i.id === selectedProduct.id) ? 'Đã thêm so sánh' : 'Thêm vào so sánh'}</span>
                        </button>

                        <button 
                          onClick={() => {
                            setIsDetailOpen(false);
                            setIsChatOpen(true);
                          }}
                          className="py-2.5 border border-blue-500 hover:bg-blue-50 text-blue-600 font-bold text-xs rounded-xl flex items-center justify-center gap-1.5 transition"
                        >
                          <Sparkles className="w-4 h-4 text-blue-500" />
                          <span>Hỏi AI về linh kiện</span>
                        </button>
                      </div>
                    </div>

                  </div>

                </div>

                {/* Bảng Specs chi tiết */}
                <div id="full-specs-table" className="border border-slate-200 rounded-2xl overflow-hidden bg-white shadow-xs">
                  <div className="flex border-b border-slate-200 bg-slate-50">
                    <button 
                      onClick={() => setDetailTab('specs')}
                      className={`flex-1 py-3 text-xs font-black uppercase transition text-center ${detailTab === 'specs' ? 'bg-white text-blue-600 border-b-2 border-blue-600' : 'text-slate-500 hover:text-slate-800'}`}
                    >
                      Thông số kỹ thuật chi tiết
                    </button>
                    <button 
                      onClick={() => setDetailTab('desc')}
                      className={`flex-1 py-3 text-xs font-black uppercase transition text-center ${detailTab === 'desc' ? 'bg-white text-blue-600 border-b-2 border-blue-600' : 'text-slate-500 hover:text-slate-800'}`}
                    >
                      Mô tả sản phẩm
                    </button>
                  </div>

                  {detailTab === 'specs' ? (
                    <div className="p-4 sm:p-6 space-y-1">
                      <div className="divide-y divide-slate-100 text-xs">
                        <div className="grid grid-cols-3 py-2.5 px-3 bg-slate-50/60 rounded">
                          <span className="text-slate-500 font-medium">Hãng sản xuất:</span>
                          <span className="col-span-2 text-slate-800 font-bold">{selectedProduct.brand}</span>
                        </div>
                        <div className="grid grid-cols-3 py-2.5 px-3">
                          <span className="text-slate-500 font-medium">Phân loại linh kiện:</span>
                          <span className="col-span-2 text-slate-800 font-bold">{selectedProduct.categoryName} ({selectedProduct.categoryType})</span>
                        </div>
                        {selectedProduct.specs?.socket && (
                          <div className="grid grid-cols-3 py-2.5 px-3 bg-slate-50/60 rounded">
                            <span className="text-slate-500 font-medium">Chân cắm Socket:</span>
                            <span className="col-span-2 text-blue-600 font-bold">{selectedProduct.specs.socket}</span>
                          </div>
                        )}
                        {selectedProduct.specs?.ramType && (
                          <div className="grid grid-cols-3 py-2.5 px-3">
                            <span className="text-slate-500 font-medium">Chuẩn RAM hỗ trợ:</span>
                            <span className="col-span-2 text-blue-600 font-bold">{selectedProduct.specs.ramType}</span>
                          </div>
                        )}
                        {selectedProduct.specs?.tdpWattage > 0 && (
                          <div className="grid grid-cols-3 py-2.5 px-3 bg-slate-50/60 rounded">
                            <span className="text-slate-500 font-medium">Công suất (TDP):</span>
                            <span className="col-span-2 text-amber-600 font-bold">{selectedProduct.specs.tdpWattage} W</span>
                          </div>
                        )}
                      </div>
                    </div>
                  ) : (
                    <div className="p-6 text-xs text-slate-600 leading-relaxed">
                      {selectedProduct.categoryDescription || 'Linh kiện phần cứng chính hãng, đầy đủ hóa đơn VAT và bảo hành tại hệ thống PC STORE toàn quốc.'}
                    </div>
                  )}
                </div>

              </div>
            )}
          </div>
        </div>
      )}

      {/* 6. MODAL CHỌN LINH KIỆN BUILDER */}
      {modalCategory && (
        <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="bg-[#0c1424] border border-cyan-900 rounded-3xl max-w-2xl w-full max-h-[80vh] flex flex-col shadow-2xl">
            <div className="p-4 border-b border-cyan-950 flex justify-between items-center bg-[#090e1a] rounded-t-3xl">
              <h3 className="text-xs font-black text-white uppercase tracking-wider">Select {modalCategory.name}</h3>
              <button onClick={() => setModalCategory(null)} className="text-slate-500 hover:text-white">
                <X className="w-5 h-5" />
              </button>
            </div>

            <div className="p-4 overflow-y-auto space-y-3 flex-1">
              {products.filter(p => p.categoryType?.toLowerCase() === modalCategory.type?.toLowerCase()).map(product => (
                <div key={product.id} className="p-3 bg-[#111a2e] hover:bg-[#16223b] border border-cyan-950 rounded-2xl flex items-center justify-between gap-4 transition">
                  <div className="flex items-center gap-3">
                    <div className="w-10 h-10 bg-[#090e1a] rounded-xl border border-cyan-950 flex items-center justify-center">
                      {renderIcon(product.categoryType)}
                    </div>
                    <div>
                      <p className="text-xs font-bold text-white">{product.name}</p>
                      <p className="text-[10px] text-slate-400">{product.brand} {product.socket ? `| Socket: ${product.socket}` : ''} {product.ramType ? `| RAM: ${product.ramType}` : ''}</p>
                    </div>
                  </div>
                  <div className="text-right shrink-0">
                    <p className="text-cyan-400 font-bold text-xs mb-1">{product.price?.toLocaleString('vi-VN')} đ</p>
                    <button onClick={() => handleSelectPart(product)} className="px-3 py-1 bg-cyan-400 text-slate-950 text-xs font-bold rounded-lg hover:bg-cyan-300">
                      Select
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </div>
        </div>
      )}

      {/* 7. MODAL HỒ SƠ & NÚT LOG OUT */}
      {isProfileOpen && (
        <div className="fixed inset-0 z-50 bg-black/80 backdrop-blur-sm flex items-center justify-center p-4">
          <div className="bg-[#0c1424] border border-cyan-900 rounded-3xl max-w-md w-full p-6 shadow-2xl relative text-slate-200">
            <button onClick={() => setIsProfileOpen(false)} className="absolute top-5 right-5 text-slate-500 hover:text-white">
              <X className="w-5 h-5" />
            </button>

            <div className="flex items-center gap-3 mb-6">
              <div className="p-2.5 rounded-xl bg-cyan-950 text-cyan-400 border border-cyan-800">
                <Settings className="w-5 h-5" />
              </div>
              <div>
                <h2 className="text-lg font-black text-white">Account Profile</h2>
                <p className="text-xs text-slate-500">{currentUser?.email}</p>
              </div>
            </div>

            {profileError && <div className="mb-4 p-3 bg-rose-950/50 border border-rose-800 text-rose-300 text-xs rounded-xl">{profileError}</div>}
            {profileSuccess && <div className="mb-4 p-3 bg-emerald-950/50 border border-emerald-800 text-emerald-300 text-xs rounded-xl">{profileSuccess}</div>}

            <form onSubmit={handleProfileSubmit} className="space-y-3.5 text-xs">
              <div>
                <label className="block text-slate-400 mb-1 font-bold">Họ và Tên</label>
                <input
                  type="text"
                  required
                  value={profileForm.fullName}
                  onChange={(e) => setProfileForm({ ...profileForm, fullName: e.target.value })}
                  className="w-full bg-[#111a2e] border border-cyan-950 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-cyan-500"
                />
              </div>

              <div>
                <label className="block text-slate-400 mb-1 font-bold">Số Điện Thoại</label>
                <input
                  type="tel"
                  value={profileForm.phoneNumber}
                  onChange={(e) => setProfileForm({ ...profileForm, phoneNumber: e.target.value })}
                  className="w-full bg-[#111a2e] border border-cyan-950 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-cyan-500"
                />
              </div>

              <div>
                <label className="block text-slate-400 mb-1 font-bold">Địa Chỉ Giao Hàng</label>
                <input
                  type="text"
                  value={profileForm.address}
                  onChange={(e) => setProfileForm({ ...profileForm, address: e.target.value })}
                  className="w-full bg-[#111a2e] border border-cyan-950 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-cyan-500"
                />
              </div>

              <div className="border-t border-cyan-950 pt-3 space-y-2">
                <span className="text-cyan-400 font-bold block">Đổi mật khẩu (Bỏ trống nếu không đổi)</span>
                <div className="grid grid-cols-2 gap-2">
                  <input
                    type="password"
                    placeholder="Mật khẩu cũ"
                    value={profileForm.currentPassword}
                    onChange={(e) => setProfileForm({ ...profileForm, currentPassword: e.target.value })}
                    className="w-full bg-[#111a2e] border border-cyan-950 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-cyan-500"
                  />
                  <input
                    type="password"
                    placeholder="Mật khẩu mới (≥6 ký tự)"
                    value={profileForm.newPassword}
                    onChange={(e) => setProfileForm({ ...profileForm, newPassword: e.target.value })}
                    className="w-full bg-[#111a2e] border border-cyan-950 rounded-xl px-3 py-2 text-white focus:outline-none focus:border-cyan-500"
                  />
                </div>
              </div>

              <div className="flex gap-2 pt-2">
                <button
                  type="submit"
                  disabled={profileLoading}
                  className="flex-1 py-3 bg-cyan-400 hover:bg-cyan-300 text-slate-950 font-black text-xs rounded-xl transition uppercase tracking-wider shadow-[0_0_10px_rgba(6,182,212,0.4)]"
                >
                  {profileLoading ? 'Saving...' : 'Save Profile Changes'}
                </button>
                
                <button
                  type="button"
                  onClick={handleLogout}
                  className="px-4 py-3 bg-rose-950/40 hover:bg-rose-900/60 text-rose-400 border border-rose-800/40 font-bold text-xs rounded-xl transition uppercase flex items-center gap-1.5"
                >
                  <LogOut className="w-3.5 h-3.5" />
                  <span>Log Out</span>
                </button>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* 8. MODAL SIGN IN & SIGN UP */}
      {isAuthOpen && (
        <div className="fixed inset-0 z-50 bg-black/85 backdrop-blur-md flex items-center justify-center p-4 animate-in fade-in zoom-in-95 duration-150">
          <div className="relative w-full max-w-[400px] rounded-[32px] p-8 bg-[#0c1424] border border-cyan-500/50 shadow-[0_0_50px_rgba(6,182,212,0.25)] overflow-hidden text-slate-200">
            <div className="absolute top-0 right-1/2 translate-x-1/2 w-48 h-24 bg-cyan-500/15 blur-2xl pointer-events-none"></div>
            <button onClick={() => setIsAuthOpen(false)} className="absolute top-5 right-5 text-slate-500 hover:text-cyan-400 transition p-1 z-10">
              <X className="w-5 h-5" />
            </button>

            <div className="flex items-center gap-4 mb-6 mt-1 relative z-10">
              <div className="w-16 h-16 rounded-full bg-gradient-to-tr from-cyan-500 to-blue-600 p-0.5 shadow-[0_0_15px_rgba(6,182,212,0.5)]">
                <div className="w-full h-full rounded-full bg-[#080d1a] flex items-center justify-center">
                  <User className="w-8 h-8 text-cyan-400" />
                </div>
              </div>
              <div>
                <h2 className="text-2xl font-black text-white tracking-wide uppercase">
                  {authTab === 'signin' ? 'Sign In' : 'Sign Up'}
                </h2>
                <div className="flex gap-2 mt-0.5 text-xs font-semibold text-slate-400">
                  <span onClick={() => { setAuthTab('signin'); setAuthError(''); setAuthSuccess(''); }} className={`cursor-pointer transition ${authTab === 'signin' ? 'text-cyan-400 font-bold border-b border-cyan-400' : 'hover:text-white'}`}>
                    Login
                  </span>
                  <span>•</span>
                  <span onClick={() => { setAuthTab('signup'); setAuthError(''); setAuthSuccess(''); }} className={`cursor-pointer transition ${authTab === 'signup' ? 'text-cyan-400 font-bold border-b border-cyan-400' : 'hover:text-white'}`}>
                    Register
                  </span>
                </div>
              </div>
            </div>

            {authError && (
              <div className="mb-4 p-3 bg-rose-950/60 border border-rose-800 text-rose-300 text-xs rounded-2xl flex items-center gap-2 font-medium">
                <AlertCircle className="w-4 h-4 shrink-0 text-rose-400" />
                <span>{authError}</span>
              </div>
            )}
            {authSuccess && (
              <div className="mb-4 p-3 bg-emerald-950/60 border border-emerald-800 text-emerald-300 text-xs rounded-2xl flex items-center gap-2 font-medium">
                <CheckCircle2 className="w-4 h-4 shrink-0 text-emerald-400" />
                <span>{authSuccess}</span>
              </div>
            )}

            <form onSubmit={handleAuthSubmit} className="space-y-3.5 relative z-10">
              {authTab === 'signup' && (
                <div className="flex items-center bg-[#111a2e] focus-within:border-cyan-400 rounded-2xl px-4 py-3 border border-cyan-950 transition shadow-inner">
                  <div className="pr-3.5 border-r border-cyan-950 text-cyan-400"><User className="w-5 h-5" /></div>
                  <input type="text" required placeholder="Full Name" value={authForm.fullName} onChange={(e) => setAuthForm({ ...authForm, fullName: e.target.value })} className="w-full bg-transparent pl-3.5 text-xs text-white placeholder-slate-500 focus:outline-none font-medium" />
                </div>
              )}

              <div className="flex items-center bg-[#111a2e] focus-within:border-cyan-400 rounded-2xl px-4 py-3 border border-cyan-950 transition shadow-inner">
                <div className="pr-3.5 border-r border-cyan-950 text-cyan-400"><Mail className="w-5 h-5" /></div>
                <input type="email" required placeholder="Email Address" value={authForm.email} onChange={(e) => setAuthForm({ ...authForm, email: e.target.value })} className="w-full bg-transparent pl-3.5 text-xs text-white placeholder-slate-500 focus:outline-none font-medium" />
              </div>

              <div className="flex items-center bg-[#111a2e] focus-within:border-cyan-400 rounded-2xl px-4 py-3 border border-cyan-950 transition shadow-inner">
                <div className="pr-3.5 border-r border-cyan-950 text-cyan-400"><Lock className="w-5 h-5" /></div>
                <input type={showPassword ? "text" : "password"} required placeholder="Password" value={authForm.password} onChange={(e) => setAuthForm({ ...authForm, password: e.target.value })} className="w-full bg-transparent pl-3.5 text-xs text-white placeholder-slate-500 focus:outline-none font-medium tracking-wider" />
                <button type="button" onClick={() => setShowPassword(!showPassword)} className="text-slate-500 hover:text-cyan-400 pl-2 transition">
                  {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                </button>
              </div>

              {authTab === 'signup' && (
                <div className="flex items-center bg-[#111a2e] focus-within:border-cyan-400 rounded-2xl px-4 py-3 border border-cyan-950 transition shadow-inner">
                  <div className="pr-3.5 border-r border-cyan-950 text-cyan-400"><Lock className="w-5 h-5" /></div>
                  <input type="password" required placeholder="Confirm Password" value={authForm.confirmPassword} onChange={(e) => setAuthForm({ ...authForm, confirmPassword: e.target.value })} className="w-full bg-transparent pl-3.5 text-xs text-white placeholder-slate-500 focus:outline-none font-medium tracking-wider" />
                </div>
              )}

              <div className="pt-2">
                <button type="submit" disabled={authLoading} className="w-full py-3.5 bg-cyan-400 hover:bg-cyan-300 text-slate-950 font-black text-xs rounded-2xl transition tracking-widest shadow-[0_0_20px_rgba(6,182,212,0.4)] uppercase">
                  {authLoading ? <Loader2 className="w-4 h-4 animate-spin mx-auto" /> : (authTab === 'signin' ? 'LOGIN' : 'SIGN UP')}
                </button>
              </div>

              <div className="flex items-center justify-between text-xs text-slate-400 pt-1 px-1 font-medium">
                <label className="flex items-center gap-2 cursor-pointer select-none">
                  <input type="checkbox" checked={rememberMe} onChange={(e) => setRememberMe(e.target.checked)} className="rounded border-cyan-950 accent-cyan-400 cursor-pointer" />
                  <span>Remember me</span>
                </label>
                <span className="hover:text-cyan-400 cursor-pointer transition">Forgot password?</span>
              </div>
            </form>
          </div>
        </div>
      )}

      {/* 9. AI HARDWARE CHATBOT (BUILD PC AI) */}
      <div className="fixed bottom-6 right-6 z-50">
        {!isChatOpen ? (
          <button 
            onClick={() => setIsChatOpen(true)}
            className="flex items-center gap-2 px-5 py-3.5 rounded-full bg-cyan-400 hover:bg-cyan-300 text-slate-950 font-black text-xs shadow-[0_0_20px_rgba(6,182,212,0.5)] transition transform hover:scale-105"
          >
            <Bot className="w-5 h-5" />
            <span>BUILD PC AI</span>
          </button>
        ) : (
          <div className="bg-[#0c1424] border border-cyan-900 rounded-3xl w-80 sm:w-96 shadow-2xl flex flex-col h-[480px] overflow-hidden">
            <div className="p-3.5 border-b border-cyan-950 flex justify-between items-center bg-[#090e1a] text-white">
              <div className="flex items-center gap-2">
                <Sparkles className="w-5 h-5 text-cyan-300 animate-pulse" />
                <span className="font-bold text-xs uppercase tracking-wider">Trợ Lý BUILD PC AI</span>
              </div>
              <button onClick={() => setIsChatOpen(false)} className="text-slate-500 hover:text-white">
                <X className="w-4 h-4" />
              </button>
            </div>
            
            <div className="p-4 flex-1 overflow-y-auto space-y-3 text-xs bg-[#070b14]">
              {messages.map((m, i) => (
                <div key={i} className={`flex ${m.sender === 'user' ? 'justify-end' : 'justify-start'}`}>
                  <div className={`max-w-[85%] p-3 rounded-2xl ${m.sender === 'user' ? 'bg-cyan-400 text-slate-950 font-semibold' : 'bg-[#111a2e] border border-cyan-950 text-slate-200'}`}>
                    {m.text}
                  </div>
                </div>
              ))}
            </div>

            <form onSubmit={handleSendMessage} className="p-3 border-t border-cyan-950 flex gap-2 bg-[#0c1424]">
              <input 
                type="text" 
                value={chatInput} 
                onChange={(e) => setChatInput(e.target.value)} 
                placeholder="Nhập ngân sách hoặc nhu cầu build PC..." 
                className="flex-1 bg-[#111a2e] border border-cyan-950 rounded-xl px-3 py-2 text-xs text-white focus:outline-none focus:border-cyan-500"
              />
              <button type="submit" className="p-2 bg-cyan-400 text-slate-950 rounded-xl font-bold hover:bg-cyan-300">
                <Send className="w-4 h-4" />
              </button>
            </form>
          </div>
        )}
      </div>

      {/* 10. FOOTER */}
      <footer className="bg-[#050811] border-t border-cyan-950/60 text-slate-500 text-xs mt-auto">
        <div className="max-w-7xl mx-auto px-4 py-8 border-b border-cyan-950/40 grid grid-cols-2 md:grid-cols-4 gap-6">
          <div className="flex items-center gap-3">
            <ShieldCheck className="w-5 h-5 text-cyan-400" />
            <div>
              <h4 className="font-bold text-white text-xs">100% Chính Hãng</h4>
              <p className="text-[10px] text-slate-500">Bảo hành 36 tháng 1 đổi 1</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <Truck className="w-5 h-5 text-cyan-400" />
            <div>
              <h4 className="font-bold text-white text-xs">Giao Hàng Nhanh</h4>
              <p className="text-[10px] text-slate-500">Miễn phí lắp đặt toàn quốc</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <RotateCcw className="w-5 h-5 text-cyan-400" />
            <div>
              <h4 className="font-bold text-white text-xs">Đổi Trả 7 Ngày</h4>
              <p className="text-[10px] text-slate-500">Lỗi kỹ thuật từ nhà sản xuất</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <Headphones className="w-5 h-5 text-cyan-400" />
            <div>
              <h4 className="font-bold text-white text-xs">BUILD PC AI 24/7</h4>
              <p className="text-[10px] text-slate-500">Tự động đối chiếu tương thích</p>
            </div>
          </div>
        </div>

        <div className="max-w-7xl mx-auto px-4 py-6 flex flex-col sm:flex-row items-center justify-between gap-4 text-[11px]">
          <span>© 2026 PC STORE. Hệ thống phân phối linh kiện máy tính & PC Gaming chính hãng.</span>
          <span>Đà Nẵng, Việt Nam • Hotline: 1900 6868</span>
        </div>
      </footer>

    </div>
  );
}