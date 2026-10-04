import React, { useState, useEffect, useCallback } from 'react';
import { 
  Cpu, ShoppingCart, Wrench, Search, ShieldCheck, Truck, 
  RotateCcw, Sparkles, ChevronRight, ChevronLeft, Layers,
  Monitor, Zap, Trash2, Plus, AlertCircle, CheckCircle2, Bot, X, ClipboardList, Clock, PackageCheck,
  Headphones, HardDrive, Fan, Box, UserPlus, LogIn, LogOut, Loader2, Mail, Lock, User, Phone, MapPin, AlertTriangle, RefreshCw, Settings, KeyRound, SlidersHorizontal, ArrowUpDown, Eye, EyeOff, Heart, Star, Compass, PhoneCall, CreditCard, Flame, Gift, Tag, Check, Award, BarChart2, Scale, Minus
} from 'lucide-react';
import { productService, authService, aiService, orderService, reviewService } from './services/api';
import StaffPanel from './components/StaffPanel';
import AdminPanel from './components/AdminPanel';

export default function App() {
  const [activeTab, setActiveTab] = useState('home'); // 'home' | 'builder' | 'ai' | 'cart' | 'checkout' | 'orders'
  const [categories, setCategories] = useState([]);
  const [products, setProducts] = useState([]);
  const [loading, setLoading] = useState(true);
  const [apiError, setApiError] = useState(null);

  // States Tìm kiếm & Lọc
  const [searchTerm, setSearchTerm] = useState('');
  const [selectedCategoryTab, setSelectedCategoryTab] = useState('ALL');
  const [priceRange, setPriceRange] = useState('ALL');
  const [sortBy, setSortBy] = useState('default');

  // State bộ lọc theo danh mục
  const [brandFilter, setBrandFilter] = useState('ALL');
  const [socketFilter, setSocketFilter] = useState('ALL');
  const [coreFilter, setCoreFilter] = useState('ALL');
  const [chipsetFilter, setChipsetFilter] = useState('ALL');
  const [ramTypeFilter, setRamTypeFilter] = useState('ALL');
  const [formFactorFilter, setFormFactorFilter] = useState('ALL');
  const [vramFilter, setVramFilter] = useState('ALL');
  const [wattageFilter, setWattageFilter] = useState('ALL');

  // State So Sánh Linh Kiện
  const [compareList, setCompareList] = useState([]); // tối đa 3 sản phẩm, cùng categoryType
  const [isCompareOpen, setIsCompareOpen] = useState(false);
  const [compareError, setCompareError] = useState('');


  // State Xem chi tiết sản phẩm
  const [selectedProduct, setSelectedProduct] = useState(null);
  const [isDetailOpen, setIsDetailOpen] = useState(false);
  const [detailLoading, setDetailLoading] = useState(false);
  const [detailTab, setDetailTab] = useState('specs'); // 'specs' | 'desc'
  const [productReviews, setProductReviews] = useState([]);
  const [reviewDrafts, setReviewDrafts] = useState({});
  const [reviewSubmittingKey, setReviewSubmittingKey] = useState('');

  // Auth State & Single Modal (Sign In / Sign Up)
  const [isAuthOpen, setIsAuthOpen] = useState(false);
  const [authTab, setAuthTab] = useState('signin'); // signin | signup | forgot | reset
  const [resetToken, setResetToken] = useState('');
  const [showPassword, setShowPassword] = useState(false);
  const [rememberMe, setRememberMe] = useState(true);

  const [isProfileOpen, setIsProfileOpen] = useState(false);
  const [isStaffPanelOpen, setIsStaffPanelOpen] = useState(false);
  const [isAdminPanelOpen, setIsAdminPanelOpen] = useState(false);
  const [orderUpdateNotice, setOrderUpdateNotice] = useState('');
  const [currentUser, setCurrentUser] = useState(() => {
    try {
      const saved = localStorage.getItem('pcstore_user');
      return saved ? JSON.parse(saved) : null;
    } catch {
      return null;
    }
  });
  const [cartItems, setCartItems] = useState(() => {
    try {
      const savedCart = JSON.parse(localStorage.getItem('pcstore_cart') || '[]');
      return Array.isArray(savedCart) ? savedCart.filter(item => item?.id && item.quantity > 0) : [];
    } catch {
      return [];
    }
  });
  const [cartNotice, setCartNotice] = useState('');
  const [checkoutForm, setCheckoutForm] = useState(() => ({
    receiverName: currentUser?.fullName || '',
    receiverPhone: currentUser?.phoneNumber || '',
    email: currentUser?.email || '',
    shippingAddress: currentUser?.address || '',
  }));
  const [checkoutLoading, setCheckoutLoading] = useState(false);
  const [checkoutError, setCheckoutError] = useState('');
  const [orderSuccess, setOrderSuccess] = useState(null);
  const [userOrders, setUserOrders] = useState([]);
  const [ordersLoading, setOrdersLoading] = useState(false);
  const [ordersError, setOrdersError] = useState('');
  const [selectedUserOrder, setSelectedUserOrder] = useState(null);
  const [orderDetailLoading, setOrderDetailLoading] = useState(false);
  const [orderDetailError, setOrderDetailError] = useState('');

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

  // Profile Form State
  const [profileForm, setProfileForm] = useState({ fullName: '', email: '', phoneNumber: '', address: '', role: '', status: '', createdAt: '', updatedAt: '' });
  const [profileLoading, setProfileLoading] = useState(false);
  const [profileError, setProfileError] = useState('');
  const [profileSuccess, setProfileSuccess] = useState('');
  const [profilePasswordForm, setProfilePasswordForm] = useState({ currentPassword: '', newPassword: '', confirmPassword: '' });
  const [profilePasswordLoading, setProfilePasswordLoading] = useState(false);

  // Builder State
  const [selectedParts, setSelectedParts] = useState({});
  const [modalCategory, setModalCategory] = useState(null);
  const [builderSearch, setBuilderSearch] = useState(''); // Thêm state tìm kiếm trong modal
  const [builderFilters, setBuilderFilters] = useState({});
  const [builderPriceRange, setBuilderPriceRange] = useState('ALL');
  const [replacementPart, setReplacementPart] = useState(null);
  const [replacementProducts, setReplacementProducts] = useState([]);
  const [replacementLoading, setReplacementLoading] = useState(false);
  const [replacementError, setReplacementError] = useState('');
  const [selectedReplacementId, setSelectedReplacementId] = useState(null);

  // BUILD PC AI
  const [aiBudgetMillions, setAiBudgetMillions] = useState(25);
  const [aiPurpose, setAiPurpose] = useState('gaming');
  const [aiSelectedCategories, setAiSelectedCategories] = useState(['CPU', 'Mainboard', 'RAM', 'GPU', 'SSD', 'PSU', 'Case']);
  const [aiNotes, setAiNotes] = useState('');
  const [aiLoading, setAiLoading] = useState(false);
  const [aiError, setAiError] = useState('');
  const [aiRecommendation, setAiRecommendation] = useState(null);

  // Tải danh sách sản phẩm từ backend API
  const [allProducts, setAllProducts] = useState([]);

  useEffect(() => {
    localStorage.setItem('pcstore_cart', JSON.stringify(cartItems));
  }, [cartItems]);

  useEffect(() => {
    if (!cartNotice) return undefined;
    const timeoutId = window.setTimeout(() => setCartNotice(''), 3000);
    return () => window.clearTimeout(timeoutId);
  }, [cartNotice]);

  const fetchProducts = useCallback(async () => {
    try {
      setLoading(true);
      setApiError(null);

      // Fetch toàn bộ theo danh mục + searchTerm + sortBy, không filter giá server-side
      // (giá được filter client-side để hiện đúng số đếm khoảng giá)
      const prodData = await productService.getProducts({
        searchTerm: searchTerm.trim(),
        categoryType: selectedCategoryTab,
        sortBy
      });

      const arr = (Array.isArray(prodData) ? prodData : []).map(p => {
        try {
          p.additionalSpecs = JSON.parse(p.additionalSpecsJson || '{}');
        } catch {
          p.additionalSpecs = {};
        }
        return p;
      });
      setAllProducts(arr);
    } catch (err) {
      setApiError('Không thể kết nối đến Backend API (http://localhost:5170). Vui lòng kiểm tra lại dịch vụ Web API.');
    } finally {
      setLoading(false);
    }
  }, [searchTerm, selectedCategoryTab, sortBy]);


  // Reset bộ lọc khi đổi danh mục
  const resetCategoryFilters = useCallback(() => {
    setBrandFilter('ALL');
    setSocketFilter('ALL');
    setCoreFilter('ALL');
    setChipsetFilter('ALL');
    setRamTypeFilter('ALL');
    setFormFactorFilter('ALL');
    setVramFilter('ALL');
    setWattageFilter('ALL');
    setPriceRange('ALL');
  }, []);

  // Áp dụng bộ lọc client-side khi filter thay đổi
  useEffect(() => {
    let filtered = [...allProducts];

    if (brandFilter !== 'ALL') {
      filtered = filtered.filter(p => p.brand === brandFilter);
    }
    if (socketFilter !== 'ALL') {
      filtered = filtered.filter(p => p.socket === socketFilter);
    }
    if (coreFilter !== 'ALL') {
      // coreFilter dạng "4", "6", "8", "12+", "16+" lấy từ additionalSpecs
      filtered = filtered.filter(p => {
        const specs = p.additionalSpecs || {};
        const cores = specs.coreCount || 0;
        if (coreFilter === '2') return cores === 2;
        if (coreFilter === '4') return cores === 4;
        if (coreFilter === '6') return cores === 6;
        if (coreFilter === '8') return cores === 8;
        if (coreFilter === '12+') return cores >= 12;
        if (coreFilter === '16+') return cores >= 16;
        return true;
      });
    }
    if (chipsetFilter !== 'ALL') {
      filtered = filtered.filter(p => p.chipset === chipsetFilter);
    }
    if (ramTypeFilter !== 'ALL') {
      filtered = filtered.filter(p => p.ramType && p.ramType.includes(ramTypeFilter));
    }
    if (formFactorFilter !== 'ALL') {
      filtered = filtered.filter(p => p.formFactor === formFactorFilter);
    }
    if (vramFilter !== 'ALL') {
      // vramFilter: "8GB", "12GB", "16GB", "24GB" - lấy từ tên
      filtered = filtered.filter(p => p.name && p.name.includes(vramFilter));
    }
    if (wattageFilter !== 'ALL') {
      filtered = filtered.filter(p => {
        const w = p.tdpWattage || 0;
        if (wattageFilter === 'UNDER_500') return w < 500;
        if (wattageFilter === '500_650') return w >= 500 && w <= 650;
        if (wattageFilter === '650_850') return w > 650 && w <= 850;
        if (wattageFilter === 'OVER_850') return w > 850;
        return true;
      });
    }
    // Lọc khoảng giá client-side
    if (priceRange !== 'ALL') {
      const priceRangeMap = {
        'UNDER_2M': [0, 2000000],
        '2M_5M': [2000000, 5000000],
        '5M_10M': [5000000, 10000000],
        '10M_20M': [10000000, 20000000],
        'OVER_20M': [20000000, Infinity],
      };
      const [minP, maxP] = priceRangeMap[priceRange] || [0, Infinity];
      filtered = filtered.filter(p => (p.price || 0) >= minP && (p.price || 0) < maxP);
    }

    setProducts(filtered);
  }, [allProducts, brandFilter, socketFilter, coreFilter, chipsetFilter, ramTypeFilter, formFactorFilter, vramFilter, wattageFilter, priceRange]);


  // Tải danh mục ban đầu
  useEffect(() => {
    const fetchInit = async () => {
      try {
        const catData = await productService.getCategories();
        setCategories(Array.isArray(catData) ? catData : []);
      } catch (err) {
        console.error('Lỗi khi tải danh mục:', err);
      }
    };
    fetchInit();
  }, []);

  useEffect(() => {
    resetCategoryFilters();
  }, [selectedCategoryTab, resetCategoryFilters]);

  useEffect(() => {
    fetchProducts();
  }, [fetchProducts]);

  // Mở modal chi tiết sản phẩm
  const handleOpenDetail = async (productId) => {
    try {
      setIsDetailOpen(true);
      setDetailLoading(true);
      setDetailTab('specs');
      setProductReviews([]);
      const [data, reviews] = await Promise.all([
        productService.getProductById(productId),
        reviewService.getReviews(productId).catch(error => {
          console.error('Không tải được đánh giá sản phẩm:', error);
          return [];
        })
      ]);
      try {
        data.additionalSpecs = JSON.parse(data.additionalSpecsJson || '{}');
      } catch {
        data.additionalSpecs = {};
      }
      setSelectedProduct(data);
      setProductReviews(Array.isArray(reviews) ? reviews : []);
    } catch (err) {
      console.error(err);
    } finally {
      setDetailLoading(false);
    }
  };

  const handleSubmitProductReview = async (orderId, productId) => {
    const key = `${orderId}:${productId}`;
    const draft = reviewDrafts[key] || { rating: 5, comment: '', imageUrl: '' };
    if (!currentUser?.id) {
      setReviewDrafts(prev => ({ ...prev, [key]: { ...draft, error: 'Vui lòng đăng nhập để gửi đánh giá.' } }));
      return;
    }

    setReviewSubmittingKey(key);
    setReviewDrafts(prev => ({ ...prev, [key]: { ...draft, error: '' } }));
    try {
      await reviewService.createReview(productId, {
        userId: currentUser.id,
        orderId,
        rating: draft.rating,
        comment: draft.comment,
        imageUrl: draft.imageUrl || null
      });
      setSelectedUserOrder(prev => prev && prev.id === orderId
        ? { ...prev, items: prev.items.map(item => item.productId === productId ? { ...item, hasReviewed: true } : item) }
        : prev);
      setUserOrders(prev => prev.map(order => order.id === orderId
        ? { ...order, items: order.items.map(item => item.productId === productId ? { ...item, hasReviewed: true } : item) }
        : order));
    } catch (error) {
      const message = error.response?.data?.message || 'Không gửi được đánh giá. Vui lòng thử lại.';
      setReviewDrafts(prev => ({ ...prev, [key]: { ...draft, error: message } }));
    } finally {
      setReviewSubmittingKey('');
    }
  };

  const handleReviewImageChange = (orderId, productId, file) => {
    const key = `${orderId}:${productId}`;
    const draft = reviewDrafts[key] || { rating: 5, comment: '', imageUrl: '' };
    if (!file) {
      setReviewDrafts(prev => ({ ...prev, [key]: { ...draft, imageUrl: '' } }));
      return;
    }
    if (!['image/jpeg', 'image/png', 'image/webp'].includes(file.type) || file.size > 3 * 1024 * 1024) {
      setReviewDrafts(prev => ({ ...prev, [key]: { ...draft, error: 'Ảnh phải là JPEG, PNG hoặc WebP và tối đa 3 MB.' } }));
      return;
    }
    const reader = new FileReader();
    reader.onload = () => setReviewDrafts(prev => ({
      ...prev,
      [key]: { ...draft, imageUrl: String(reader.result), error: '' }
    }));
    reader.onerror = () => setReviewDrafts(prev => ({
      ...prev,
      [key]: { ...draft, error: 'Không đọc được ảnh đã chọn.' }
    }));
    reader.readAsDataURL(file);
  };

  // Thêm / xóa sản phẩm khỏi danh sách so sánh
  const handleToggleCompare = async (product) => {
    setCompareError('');
    const alreadyIn = compareList.find(p => p.id === product.id);

    if (alreadyIn) {
      setCompareList(prev => prev.filter(p => p.id !== product.id));
      return;
    }

    // Kiểm tra cùng loại danh mục
    if (compareList.length > 0 && compareList[0].categoryType !== product.categoryType) {
      setCompareError(`Chỉ so sánh được sản phẩm cùng loại! (Đang chọn: ${compareList[0].categoryType})`);
      setTimeout(() => setCompareError(''), 3000);
      return;
    }

    // Tối đa 3 sản phẩm
    if (compareList.length >= 3) {
      setCompareError('Chỉ so sánh tối đa 3 sản phẩm cùng lúc!');
      setTimeout(() => setCompareError(''), 3000);
      return;
    }

    // Fetch full product data để đảm bảo có đủ additionalSpecs
    try {
      const fullData = await productService.getProductById(product.id);
      try {
        fullData.additionalSpecs = JSON.parse(fullData.additionalSpecsJson || '{}');
      } catch {
        fullData.additionalSpecs = {};
      }
      // Merge thêm fields từ list (categoryType, etc.)
      fullData.categoryType = product.categoryType || fullData.categoryType;
      setCompareList(prev => [...prev, fullData]);
    } catch {
      // Fallback: dùng data từ list nếu fetch lỗi
      const fallback = { ...product };
      try { fallback.additionalSpecs = JSON.parse(product.additionalSpecsJson || '{}'); } catch { fallback.additionalSpecs = {}; }
      setCompareList(prev => [...prev, fallback]);
    }
  };

  // Mở hồ sơ người dùng
  const handleOpenProfile = async () => {
    if (!currentUser) return;
    setProfileError('');
    setProfileSuccess('');
    setProfileForm({
      fullName: currentUser.fullName || '',
      email: currentUser.email || '',
      phoneNumber: currentUser.phoneNumber || '',
      address: currentUser.address || '',
      role: currentUser.role || '',
      status: currentUser.status || '',
      createdAt: currentUser.createdAt || '',
      updatedAt: currentUser.updatedAt || ''
    });
    setProfilePasswordForm({ currentPassword: '', newPassword: '', confirmPassword: '' });
    setIsProfileOpen(true);

    try {
      const data = await authService.getProfile(currentUser.id);
      setProfileForm(prev => ({
        ...prev,
        fullName: data.fullName || '',
        email: data.email || '',
        phoneNumber: data.phoneNumber || '',
        address: data.address || '',
        role: data.role || '',
        status: data.status || '',
        createdAt: data.createdAt || '',
        updatedAt: data.updatedAt || ''
      }));
    } catch (err) {
      console.warn('Lỗi lấy hồ sơ:', err);
    }
  };

  // Lưu hồ sơ
  const handleProfileSubmit = async (e) => {
    e.preventDefault();
    setProfileError('');
    setProfileSuccess('');

    try {
      setProfileLoading(true);
      const res = await authService.updateProfile(currentUser.id, {
        fullName: profileForm.fullName,
        phoneNumber: profileForm.phoneNumber,
        address: profileForm.address
      });
      setProfileSuccess(res.message || 'Cập nhật thông tin thành công!');
      setCurrentUser(prev => ({ ...prev, ...res.user }));
      localStorage.setItem('pcstore_user', JSON.stringify({ ...currentUser, ...res.user }));
    } catch (err) {
      setProfileError(err.response?.data?.detail || err.response?.data?.message || 'Lỗi cập nhật hồ sơ.');
    } finally {
      setProfileLoading(false);
    }
  };

  const handleProfilePasswordSubmit = async (e) => {
    e.preventDefault();
    setProfileError('');
    setProfileSuccess('');

    if (profilePasswordForm.newPassword.length < 8) {
      setProfileError('Mật khẩu mới phải có ít nhất 8 ký tự.');
      return;
    }
    if (profilePasswordForm.newPassword !== profilePasswordForm.confirmPassword) {
      setProfileError('Mật khẩu xác nhận không khớp.');
      return;
    }

    try {
      setProfilePasswordLoading(true);
      const result = await authService.changePassword(currentUser.id, profilePasswordForm);
      setProfilePasswordForm({ currentPassword: '', newPassword: '', confirmPassword: '' });
      setProfileSuccess(result.message || 'Đổi mật khẩu thành công.');
    } catch (err) {
      setProfileError(err.response?.data?.message || 'Không thể đổi mật khẩu.');
    } finally {
      setProfilePasswordLoading(false);
    }
  };

  // Đăng ký / Đăng nhập
  const handleAuthSubmit = async (e) => {
    e.preventDefault();
    setAuthError('');
    setAuthSuccess('');

    if (authTab === 'forgot') {
      try {
        setAuthLoading(true);
        const res = await authService.requestPasswordReset({ email: authForm.email });
        setAuthSuccess(res.message || 'Nếu email tồn tại, hướng dẫn đặt lại sẽ được gửi.');
        if (res.developmentResetToken) {
          setResetToken(res.developmentResetToken);
          setAuthTab('reset');
          setAuthForm(prev => ({ ...prev, password: '', confirmPassword: '' }));
        }
      } catch (err) {
        setAuthError(err.response?.data?.message || 'Không thể yêu cầu đặt lại mật khẩu.');
      } finally {
        setAuthLoading(false);
      }
      return;
    }

    if (authTab === 'reset') {
      if (authForm.password.length < 8) {
        setAuthError('Mật khẩu mới phải có ít nhất 8 ký tự.');
        return;
      }
      if (authForm.password !== authForm.confirmPassword) {
        setAuthError('Mật khẩu xác nhận không khớp.');
        return;
      }

      try {
        setAuthLoading(true);
        const res = await authService.resetPassword({
          email: authForm.email,
          resetToken,
          newPassword: authForm.password
        });
        setAuthSuccess(res.message || 'Đặt lại mật khẩu thành công.');
        setAuthTab('signin');
        setResetToken('');
        setAuthForm(prev => ({ ...prev, password: '', confirmPassword: '' }));
      } catch (err) {
        setAuthError(err.response?.data?.message || 'Không thể đặt lại mật khẩu.');
      } finally {
        setAuthLoading(false);
      }
      return;
    }

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

  // Đăng xuất
  const handleLogout = async () => {
    try {
      await authService.logout();
    } catch (err) {
      console.warn('Lỗi logout:', err);
    } finally {
      localStorage.removeItem('pcstore_user');
      setCurrentUser(null);
      setUserOrders([]);
      setSelectedUserOrder(null);
      if (activeTab === 'orders') setActiveTab('home');
      setIsProfileOpen(false);
    }
  };

  // Thuật toán kiểm tra tính tương thích phần cứng
  const totalPrice = Object.values(selectedParts).reduce((sum, item) => sum + (item?.price || 0), 0);
  const estimatedTdp = (selectedParts['CPU']?.tdpWattage || 0) + 
                       (selectedParts['GPU']?.tdpWattage || 0) + 
                       (selectedParts['RAM']?.tdpWattage || 10) + 50;
  const psuWattage = selectedParts['PSU']?.tdpWattage || selectedParts['PSU']?.recommendedPsu || 0;

  const getCompatibilityErrors = (parts) => {
    const errors = [];
    const partsCpu = parts.CPU;
    const partsMainboard = parts.Mainboard;
    const partsRam = parts.RAM;
    const partsPsuWattage = parts.PSU?.tdpWattage || parts.PSU?.recommendedPsu || 0;
    const partsEstimatedTdp = (parts.CPU?.tdpWattage || 0) +
      (parts.GPU?.tdpWattage || 0) +
      (parts.RAM?.tdpWattage || 10) + 50;

    if (partsCpu && partsMainboard && partsCpu.socket && partsMainboard.socket &&
      partsCpu.socket.trim().toLowerCase() !== partsMainboard.socket.trim().toLowerCase()) {
      errors.push(`Cảnh báo Socket: CPU (${partsCpu.socket}) không tương thích với chân cắm Mainboard (${partsMainboard.socket})!`);
    }
    if (partsMainboard && partsRam && partsMainboard.ramType && partsRam.ramType &&
      !partsMainboard.ramType.toLowerCase().includes(partsRam.ramType.toLowerCase())) {
      errors.push(`Cảnh báo RAM: Bo mạch chủ hỗ trợ (${partsMainboard.ramType}) nhưng bạn đang chọn RAM (${partsRam.ramType})!`);
    }
    if (partsPsuWattage > 0 && partsEstimatedTdp > partsPsuWattage) {
      errors.push(`Thiếu nguồn: Công suất tiêu thụ ước tính (${partsEstimatedTdp}W) vượt ngưỡng chịu tải của nguồn (${partsPsuWattage}W)!`);
    }
    return errors;
  };

  const compatibilityErrors = getCompatibilityErrors(selectedParts);
  const cpu = selectedParts.CPU;
  const mb = selectedParts.Mainboard;
  const ram = selectedParts.RAM;

  const handleSelectPart = (product) => {
    setSelectedParts(prev => ({ ...prev, [product.categoryType]: product }));
    setModalCategory(null);
    setBuilderSearch('');
    setBuilderFilters({});
    setBuilderPriceRange('ALL');
    setReplacementPart(null);
    setSelectedReplacementId(null);
  };

  const addProductsToCart = (productsToAdd, successMessage) => {
    const productsToAddFiltered = productsToAdd.filter(Boolean);
    if (productsToAddFiltered.length === 0) {
      setCartNotice('Chưa có linh kiện nào để thêm vào giỏ.');
      return;
    }
    const unavailableProduct = productsToAddFiltered.find(product => (product.stockQuantity || 0) < 1);
    if (unavailableProduct) {
      setCartNotice(`${unavailableProduct.name} hiện đã hết hàng.`);
      return;
    }

    setCartItems(currentItems => {
      const updatedItems = [...currentItems];
      for (const product of productsToAddFiltered) {
        const existingIndex = updatedItems.findIndex(item => item.id === product.id);
        if (existingIndex >= 0) {
          const existingItem = updatedItems[existingIndex];
          if (existingItem.quantity >= (product.stockQuantity || 0)) continue;
          updatedItems[existingIndex] = {
            ...existingItem,
            ...product,
            quantity: Math.min(existingItem.quantity + 1, product.stockQuantity || existingItem.quantity + 1),
          };
        } else {
          updatedItems.push({ ...product, quantity: 1 });
        }
      }
      return updatedItems;
    });
    setOrderSuccess(null);
    setCartNotice(successMessage);
  };

  const handleAddProductToCart = product => addProductsToCart([product], 'Đã thêm sản phẩm vào giỏ hàng.');

  const handleAddBuildToCart = () => {
    if (compatibilityErrors.length > 0) {
      setCartNotice('Hãy xử lý các cảnh báo tương thích trước khi thêm cấu hình vào giỏ.');
      return;
    }
    addProductsToCart(Object.values(selectedParts), 'Đã thêm cấu hình PC vào giỏ hàng.');
  };

  const handleUpdateCartQuantity = (productId, quantity) => {
    setCartItems(currentItems => currentItems.flatMap(item => {
      if (item.id !== productId) return [item];
      if (quantity <= 0) return [];
      return [{ ...item, quantity: Math.min(quantity, item.stockQuantity || quantity) }];
    }));
  };

  const handleCheckout = async event => {
    event.preventDefault();
    setCheckoutError('');
    if (cartItems.length === 0) {
      setCheckoutError('Giỏ hàng đang trống.');
      return;
    }

    try {
      setCheckoutLoading(true);
      const result = await orderService.createOrder({
        userId: currentUser?.id || null,
        receiverName: checkoutForm.receiverName.trim(),
        receiverPhone: checkoutForm.receiverPhone.trim(),
        email: checkoutForm.email.trim(),
        shippingAddress: checkoutForm.shippingAddress.trim(),
        paymentMethod: 'COD',
        items: cartItems.map(item => ({ productId: item.id, quantity: item.quantity })),
      });
      setCartItems([]);
      setOrderSuccess(result);
      setActiveTab('cart');
    } catch (error) {
      console.error('Lỗi khi đặt hàng từ giỏ hàng:', error);
      setCheckoutError(error.response?.data?.message || 'Không thể đặt hàng lúc này. Vui lòng thử lại.');
    } finally {
      setCheckoutLoading(false);
    }
  };

  const handleOpenUserOrders = async () => {
    if (!currentUser?.id) {
      setAuthTab('signin');
      setIsAuthOpen(true);
      return;
    }

    setActiveTab('orders');
    setSelectedUserOrder(null);
    setOrdersError('');
    setOrderUpdateNotice('');
    setOrdersLoading(true);
    try {
      const orders = await orderService.getUserOrders(currentUser.id);
      setUserOrders(Array.isArray(orders) ? orders : []);
    } catch (error) {
      console.error('Lỗi khi tải danh sách đơn hàng:', error);
      setOrdersError(error.response?.data?.message || 'Không thể tải đơn hàng. Vui lòng thử lại.');
    } finally {
      setOrdersLoading(false);
    }
  };

  const handleOpenUserOrder = async orderId => {
    setSelectedUserOrder(null);
    setOrderDetailError('');
    setOrderDetailLoading(true);
    try {
      const order = await orderService.getUserOrderById(currentUser.id, orderId);
      setSelectedUserOrder(order);
    } catch (error) {
      console.error('Lỗi khi tải chi tiết đơn hàng:', error);
      setOrderDetailError(error.response?.data?.message || 'Không thể tải chi tiết đơn hàng.');
    } finally {
      setOrderDetailLoading(false);
    }
  };

  useEffect(() => {
    if (activeTab !== 'orders' || !currentUser?.id) return undefined;
    let active = true;
    let connection;
    const connect = async () => {
      try {
        const connected = await orderService.connectToNewOrderNotifications(
          undefined,
          undefined,
          async () => {
            if (!active) return;
            setOrderUpdateNotice('Trạng thái đơn hàng vừa được cập nhật.');
            try {
              const orders = await orderService.getUserOrders(currentUser.id);
              if (!active) return;
              setUserOrders(Array.isArray(orders) ? orders : []);
              if (selectedUserOrder?.id) {
                const updatedOrder = await orderService.getUserOrderById(currentUser.id, selectedUserOrder.id);
                if (active) setSelectedUserOrder(updatedOrder);
              }
            } catch (error) {
              console.error('Không thể làm mới trạng thái đơn hàng realtime:', error);
              if (active) setOrderUpdateNotice('Có cập nhật đơn hàng nhưng chưa tải được chi tiết mới. Vui lòng làm mới.');
            }
          }
        );
        if (!active) {
          await connected.stop();
          return;
        }
        connection = connected;
      } catch (error) {
        console.error('Không kết nối được thông báo trạng thái đơn hàng:', error);
      }
    };
    connect();
    return () => {
      active = false;
      connection?.stop();
    };
  }, [activeTab, currentUser?.id, selectedUserOrder?.id]);

  const normalizeProducts = (products) => (Array.isArray(products) ? products : []).map(product => {
    try {
      product.additionalSpecs = JSON.parse(product.additionalSpecsJson || '{}');
    } catch {
      product.additionalSpecs = {};
    }
    return product;
  });

  const handleOpenReplacementSuggestions = async (part) => {
    setReplacementPart(part);
    setReplacementProducts([]);
    setSelectedReplacementId(null);
    setReplacementError('');
    setReplacementLoading(true);

    try {
      const products = await productService.getProducts({ categoryType: part.categoryType });
      setReplacementProducts(normalizeProducts(products));
    } catch (error) {
      console.error('Lỗi khi tải linh kiện thay thế:', error);
      setReplacementError('Không thể tải danh sách linh kiện thay thế. Vui lòng thử lại.');
    } finally {
      setReplacementLoading(false);
    }
  };

  const getEstimatedPerformance = (product) => {
    const specs = product.additionalSpecs || {};
    const numericValue = value => {
      const parsed = Number.parseFloat(String(value ?? '').replace(',', '.'));
      return Number.isFinite(parsed) ? parsed : 0;
    };

    switch (product.categoryType) {
      case 'CPU': {
        const cores = numericValue(specs.coreCount);
        const clock = numericValue(specs.boostClock || specs.baseClock);
        return cores && clock ? { score: cores * clock, basis: 'Số nhân và xung nhịp' } : null;
      }
      case 'RAM': {
        const capacity = numericValue(specs.capacityGb);
        const speed = numericValue(product.ramBusSpeed);
        return capacity && speed ? { score: capacity * speed, basis: 'Dung lượng và tốc độ RAM' } : null;
      }
      case 'SSD':
      case 'HDD': {
        const read = numericValue(specs.readSpeed);
        const write = numericValue(specs.writeSpeed);
        return read || write ? { score: read + write, basis: 'Tốc độ đọc và ghi' } : null;
      }
      case 'PSU': {
        const wattage = numericValue(specs.wattage || product.tdpWattage);
        return wattage ? { score: wattage, basis: 'Công suất nguồn' } : null;
      }
      case 'Cooler': {
        const coolingCapacity = numericValue(specs.tdpSupport || product.tdpWattage);
        return coolingCapacity ? { score: coolingCapacity, basis: 'Công suất tản nhiệt' } : null;
      }
      default: {
        const benchmark = numericValue(specs.performanceIndex || specs.benchmarkScore);
        return benchmark ? { score: benchmark, basis: 'Điểm hiệu năng' } : null;
      }
    }
  };

  const getReplacementSuggestions = () => {
    if (!replacementPart?.price) return [];

    const minPrice = replacementPart.price * 0.8;
    const maxPrice = replacementPart.price * 1.2;

    return replacementProducts
      .filter(product =>
        product.id !== replacementPart.id &&
        product.categoryType === replacementPart.categoryType &&
        product.stockQuantity > 0 &&
        product.price >= minPrice &&
        product.price <= maxPrice
      )
      .map(product => {
        const candidateParts = { ...selectedParts, [replacementPart.categoryType]: product };
        const candidateErrors = getCompatibilityErrors(candidateParts);
        const currentPerformance = getEstimatedPerformance(replacementPart);
        const candidatePerformance = getEstimatedPerformance(product);
        const hasComparablePerformance = currentPerformance && candidatePerformance;
        const performanceChange = hasComparablePerformance
          ? ((candidatePerformance.score / currentPerformance.score) - 1) * 100
          : ((product.price / replacementPart.price) - 1) * 100;

        return {
          ...product,
          compatibilityErrors: candidateErrors,
          performanceChange,
          performanceBasis: hasComparablePerformance
            ? currentPerformance.basis
            : 'Ước tính tham chiếu theo mức giá (chưa có benchmark)',
        };
      })
      .filter(product => product.compatibilityErrors.length === 0)
      .sort((a, b) => b.performanceChange - a.performanceChange)
      .slice(0, 5);
  };

  const handleConfirmReplacement = () => {
    const product = getReplacementSuggestions().find(item => item.id === selectedReplacementId);
    if (!product) return;
    handleSelectPart(product);
  };

  const handleRemovePart = (categoryType) => {
    setSelectedParts(prev => {
      const updated = { ...prev };
      delete updated[categoryType];
      return updated;
    });
  };

  // Lọc sản phẩm tương thích trong Modal Chọn Linh Kiện
  const getCompatibleProducts = (categoryType) => {
    let list = allProducts.filter(p => p.categoryType?.toLowerCase() === categoryType.toLowerCase());

    // Ràng buộc tính tương thích
    if (categoryType === 'CPU' && mb?.socket) {
      list = list.filter(p => p.socket?.trim().toLowerCase() === mb.socket.trim().toLowerCase());
    }
    if (categoryType === 'Mainboard') {
      if (cpu?.socket) {
        list = list.filter(p => p.socket?.trim().toLowerCase() === cpu.socket.trim().toLowerCase());
      }
      if (ram?.ramType) {
        // Mainboard ramType có thể là "DDR4, DDR5", cần chứa loại của RAM
        list = list.filter(p => p.ramType?.toLowerCase().includes(ram.ramType.toLowerCase()));
      }
    }
    if (categoryType === 'RAM' && mb?.ramType) {
      list = list.filter(p => mb.ramType.toLowerCase().includes(p.ramType?.toLowerCase()));
    }

    return list;
  };

  const getBuilderFilterFields = (categoryType) => {
    const fieldsByCategory = {
      CPU: [
        { key: 'socket', label: 'Socket', getValue: p => p.socket },
        { key: 'coreCount', label: 'Số nhân', getValue: p => p.additionalSpecs?.coreCount, unit: ' nhân' },
      ],
      Mainboard: [
        { key: 'socket', label: 'Socket', getValue: p => p.socket },
        { key: 'chipset', label: 'Chipset', getValue: p => p.chipset },
        { key: 'ramType', label: 'Chuẩn RAM', getValue: p => p.ramType },
        { key: 'formFactor', label: 'Kích thước', getValue: p => p.formFactor },
      ],
      RAM: [
        { key: 'ramType', label: 'Chuẩn RAM', getValue: p => p.ramType },
        { key: 'capacityGb', label: 'Dung lượng', getValue: p => p.additionalSpecs?.capacityGb, unit: ' GB' },
        { key: 'ramBusSpeed', label: 'Tốc độ', getValue: p => p.ramBusSpeed, unit: ' MHz' },
      ],
      GPU: [
        { key: 'vramGb', label: 'VRAM', getValue: p => p.additionalSpecs?.vramGb, unit: ' GB' },
      ],
      SSD: [
        { key: 'interfaceType', label: 'Giao tiếp', getValue: p => p.additionalSpecs?.interfaceType },
        { key: 'capacityGb', label: 'Dung lượng', getValue: p => p.additionalSpecs?.capacityGb, unit: ' GB' },
        { key: 'formFactor', label: 'Kích thước', getValue: p => p.formFactor || p.additionalSpecs?.formFactor },
      ],
      HDD: [
        { key: 'interfaceType', label: 'Giao tiếp', getValue: p => p.additionalSpecs?.interfaceType },
        { key: 'capacityGb', label: 'Dung lượng', getValue: p => p.additionalSpecs?.capacityGb, unit: ' GB' },
        { key: 'formFactor', label: 'Kích thước', getValue: p => p.formFactor || p.additionalSpecs?.formFactor },
      ],
      PSU: [
        { key: 'wattage', label: 'Công suất', getValue: p => p.tdpWattage || p.additionalSpecs?.wattage, unit: ' W' },
        { key: 'efficiencyRating', label: 'Hiệu suất', getValue: p => p.additionalSpecs?.efficiencyRating },
        { key: 'modularType', label: 'Dây nguồn', getValue: p => p.additionalSpecs?.modularType },
      ],
      Case: [
        { key: 'formFactor', label: 'Kích thước', getValue: p => p.formFactor },
      ],
      Cooler: [
        { key: 'coolingType', label: 'Loại tản nhiệt', getValue: p => p.additionalSpecs?.coolingType },
        { key: 'socket', label: 'Socket hỗ trợ', getValue: p => p.socket },
      ],
    };

    return [
      { key: 'brand', label: 'Thương hiệu', getValue: p => p.brand },
      ...(fieldsByCategory[categoryType] || []),
    ];
  };

  const getFilteredBuilderProducts = (categoryType, compatibleProducts) => {
    let list = compatibleProducts;

    // Lọc theo tìm kiếm trong modal
    if (builderSearch.trim()) {
      const term = builderSearch.trim().toLowerCase();
      list = list.filter(p => p.name?.toLowerCase().includes(term) || p.brand?.toLowerCase().includes(term));
    }

    const filterFields = getBuilderFilterFields(categoryType);
    for (const field of filterFields) {
      const selectedValue = builderFilters[field.key];
      if (selectedValue && selectedValue !== 'ALL') {
        list = list.filter(product => {
          const value = field.getValue(product);
          if (value == null) return false;
          return field.key === 'ramType'
            ? String(value).toLowerCase().includes(selectedValue.toLowerCase())
            : String(value) === selectedValue;
        });
      }
    }

    const priceRanges = {
      UNDER_2M: [0, 2000000],
      '2M_5M': [2000000, 5000000],
      '5M_10M': [5000000, 10000000],
      '10M_20M': [10000000, 20000000],
      OVER_20M: [20000000, Infinity],
    };
    if (builderPriceRange !== 'ALL') {
      const [minPrice, maxPrice] = priceRanges[builderPriceRange];
      list = list.filter(product => product.price >= minPrice && product.price < maxPrice);
    }

    return list;
  };

  const handleGenerateAiBuild = async (e) => {
    e.preventDefault();
    setAiError('');
    setAiRecommendation(null);
    if (!Number.isFinite(Number(aiBudgetMillions)) || Number(aiBudgetMillions) < 1) {
      setAiError('Ngân sách phải từ 1 triệu đồng trở lên.');
      return;
    }
    if (aiSelectedCategories.length === 0) {
      setAiError('Vui lòng chọn ít nhất một loại linh kiện cần mua.');
      return;
    }
    if (aiNotes.length > 500) {
      setAiError('Ghi chú không được vượt quá 500 ký tự.');
      return;
    }

    try {
      setAiLoading(true);
      const result = await aiService.recommendBuild({
        budget: Number(aiBudgetMillions) * 1000000,
        purpose: aiPurpose,
        categoryTypes: aiSelectedCategories,
        notes: aiNotes.trim(),
      });
      if (!result.recommendation) {
        setAiError(result.message || 'Chưa tìm được cấu hình phù hợp. Hãy thử tăng ngân sách hoặc chọn ít linh kiện hơn.');
        return;
      }
      setAiRecommendation(result.recommendation);
    } catch (error) {
      console.error('Lỗi khi tạo cấu hình BUILD PC AI:', error);
      if (error.response?.status === 404) {
        setAiError('Backend đang chạy phiên bản cũ chưa có chức năng BUILD PC AI. Hãy dừng và khởi động lại PCStore.API để nạp endpoint mới.');
      } else if (error.response?.data?.message) {
        setAiError(error.response.data.message);
      } else if (error.code === 'ERR_NETWORK') {
        setAiError('Không kết nối được Backend API. Hãy kiểm tra PCStore.API tại http://127.0.0.1:5170.');
      } else {
        setAiError('Không thể tạo cấu hình lúc này. Vui lòng thử lại sau khi kiểm tra Backend API.');
      }
    } finally {
      setAiLoading(false);
    }
  };

  const handleUseAiBuild = () => {
    if (!aiRecommendation?.items) return;
    setSelectedParts(Object.fromEntries(aiRecommendation.items.map(item => [item.categoryType, item])));
    setActiveTab('builder');
  };

  const renderIcon = (type) => {
    switch (type) {
      case 'CPU': return <Cpu className="w-5 h-5 text-blue-600" />;
      case 'Mainboard': return <Layers className="w-5 h-5 text-blue-600" />;
      case 'RAM': return <HardDrive className="w-5 h-5 text-blue-600" />;
      case 'GPU': return <Monitor className="w-5 h-5 text-blue-600" />;
      case 'SSD': return <HardDrive className="w-5 h-5 text-blue-600" />;
      case 'PSU': return <Zap className="w-5 h-5 text-blue-600" />;
      case 'Case': return <Box className="w-5 h-5 text-blue-600" />;
      case 'Cooler': return <Fan className="w-5 h-5 text-blue-600" />;
      default: return <Cpu className="w-5 h-5 text-blue-600" />;
    }
  };

  return (
    <div className="min-h-screen bg-[#f4f6f8] text-slate-800 flex flex-col font-sans selection:bg-blue-600 selection:text-white">
      
      {/* 1. TOP UTILITY BAR (Chuẩn các trang e-Commerce Việt Nam) */}
      <div className="bg-[#a80f18] text-white text-[11px] py-1.5 border-b border-red-900/30">
        <div className="max-w-7xl mx-auto px-4 flex justify-between items-center">
          <div className="hidden sm:flex items-center gap-6">
            <span className="flex items-center gap-1">
              <MapPin className="w-3.5 h-3.5 text-amber-300" /> Hệ thống Showroom: Đà Nẵng - Hà Nội - TP.HCM
            </span>
            <span className="hidden md:inline-flex items-center gap-1 text-red-100">
              <ShieldCheck className="w-3.5 h-3.5 text-emerald-400" /> Cam kết 100% linh kiện chính hãng full VAT
            </span>
          </div>
          <div className="flex w-full sm:w-auto justify-center sm:justify-end">
            <span className="flex items-center gap-1 font-semibold text-amber-300 text-[10px] sm:text-[11px]">
              <PhoneCall className="w-3.5 h-3.5" /> Hotline: 1900 6868 (8:00 - 21:30)
            </span>
          </div>
        </div>
      </div>

      {/* 2. MAIN HEADER (Form Nguyễn Công PC / An Phát PC) */}
      <header className="sticky top-0 z-40 bg-[#d71920] text-white shadow-md">
        <div className="max-w-7xl mx-auto px-4 min-h-20 py-2 flex flex-wrap sm:flex-nowrap items-center justify-between gap-2 sm:gap-4">
          
          {/* Logo Brand */}
          <div className="flex items-center gap-2 sm:gap-3 cursor-pointer select-none shrink-0" onClick={() => setActiveTab('home')}>
            <div className="w-9 h-9 sm:w-12 sm:h-12 rounded-md bg-white flex items-center justify-center shadow-md">
              <Cpu className="w-6 h-6 sm:w-7 sm:h-7 text-[#d71920]" />
            </div>
            <div className="leading-tight">
              <div className="text-lg sm:text-2xl font-black tracking-tight text-white uppercase drop-shadow-sm">
                PC<span className="text-amber-300">STORE</span>
              </div>
              <div className="hidden sm:block text-[10px] tracking-widest text-red-100 uppercase font-bold">
                Công Nghệ Hàng Đầu
              </div>
            </div>
          </div>

          {/* Ô Tìm Kiếm Bo Tròn Trung Tâm */}
          <div className="order-3 basis-full sm:order-none sm:basis-auto flex-1 max-w-none sm:max-w-xl relative">
            <input
              type="text"
              placeholder="Bạn cần tìm linh kiện máy tính gì hôm nay (CPU, RTX 4070, RAM...)..."
              value={searchTerm}
              onChange={(e) => setSearchTerm(e.target.value)}
              className="w-full bg-white text-slate-800 rounded-md pl-5 pr-12 py-3 text-xs md:text-sm font-medium focus:outline-none shadow-md placeholder-slate-400"
            />
            {searchTerm && (
              <button onClick={() => setSearchTerm('')} className="absolute right-11 top-3.5 text-slate-400 hover:text-slate-600">
                <X className="w-4 h-4" />
              </button>
            )}
            <button 
              onClick={() => {
                setActiveTab('home');
                document.getElementById('catalog-section')?.scrollIntoView({ behavior: 'smooth' });
              }}
              className="absolute right-3 top-2.5 p-1 rounded bg-[#a80f18] text-white hover:bg-red-800 transition"
            >
              <Search className="w-5 h-5" />
            </button>
          </div>

          {/* Cụm 5 Nút Thao Tác Chuẩn Bàn Cờ */}
          <div className="flex items-center gap-1 sm:gap-2 shrink-0 text-white select-none">
            
            {/* 1. Xây Dựng Cấu Hình */}
            <div 
              onClick={() => setActiveTab('builder')}
              className={`flex flex-col items-center justify-center px-1.5 sm:px-2.5 py-1.5 rounded-xl cursor-pointer transition ${
                activeTab === 'builder' ? 'bg-white/20' : 'hover:bg-white/10'
              }`}
            >
              <Wrench className="w-5 h-5 text-white" />
              <span className="hidden sm:block text-[11px] font-bold mt-1 text-center whitespace-nowrap leading-tight">
                Xây Dựng PC
              </span>
            </div>

            {/* 2. Hotline / Liên Hệ */}
            <div 
              onClick={() => alert('Hotline hỗ trợ kỹ thuật và mua hàng: 1900 6868 (Phím 1: Tư vấn bán hàng, Phím 2: Hỗ trợ kỹ thuật)')}
              className="hidden sm:flex flex-col items-center justify-center px-2.5 py-1.5 rounded-xl cursor-pointer hover:bg-white/10 transition"
            >
              <PhoneCall className="w-5 h-5 text-white" />
              <span className="text-[11px] font-bold mt-1 text-center whitespace-nowrap leading-tight">
                Liên Hệ
              </span>
            </div>

            {/* 3. BUILD PC AI (Điểm nhấn sáng tạo) */}
            <div 
              onClick={() => setActiveTab('ai')}
              className={`flex flex-col items-center justify-center px-1.5 sm:px-3 py-1.5 rounded-xl cursor-pointer text-slate-900 transition shadow-md group ${
                activeTab === 'ai' ? 'bg-amber-300' : 'bg-amber-400 hover:bg-amber-300'
              }`}
              title="Tạo cấu hình PC theo ngân sách và mục đích sử dụng"
            >
              <Sparkles className="w-5 h-5 text-slate-900 group-hover:scale-110 transition animate-pulse" />
              <span className="hidden sm:block text-[11px] font-black mt-1 text-center whitespace-nowrap leading-tight">
                BUILD PC AI
              </span>
            </div>

            {/* 4. Giỏ Hàng */}
            <div 
              onClick={() => setActiveTab('cart')}
              className={`flex flex-col items-center justify-center px-1.5 sm:px-2.5 py-1.5 rounded-xl cursor-pointer transition relative ${
                activeTab === 'cart' ? 'bg-white/20' : 'hover:bg-white/10'
              }`}
            >
              <div className="relative">
                <ShoppingCart className="w-5 h-5 text-white" />
                {cartItems.length > 0 && (
                  <span className="absolute -top-1.5 -right-2.5 bg-red-600 text-white rounded-full w-4 h-4 flex items-center justify-center text-[10px] font-black border border-white">
                    {cartItems.reduce((count, item) => count + item.quantity, 0)}
                  </span>
                )}
              </div>
              <span className="hidden sm:block text-[11px] font-bold mt-1 text-center whitespace-nowrap leading-tight">
                Giỏ Hàng
              </span>
            </div>

            {currentUser && (
              <div
                role="button"
                tabIndex={0}
                onClick={handleOpenUserOrders}
                onKeyDown={event => {
                  if (event.key === 'Enter' || event.key === ' ') handleOpenUserOrders();
                }}
                className={`flex flex-col items-center justify-center px-1.5 sm:px-2.5 py-1.5 rounded-xl cursor-pointer transition ${
                  activeTab === 'orders' ? 'bg-white/20' : 'hover:bg-white/10'
                }`}
              >
                <ClipboardList className="w-5 h-5 text-white" />
                <span className="hidden sm:block text-[11px] font-bold mt-1 text-center whitespace-nowrap leading-tight">
                  Đơn hàng
                </span>
              </div>
            )}

            {['Staff', 'Admin', 'Manager'].includes(currentUser?.role) && (
              <button
                type="button"
                onClick={() => currentUser.role === 'Staff'
                  ? setIsStaffPanelOpen(true)
                  : setIsAdminPanelOpen(true)}
                className="flex flex-col items-center justify-center rounded-xl px-1.5 py-1.5 transition hover:bg-white/10"
                title="Mở bảng điều khiển xử lý đơn hàng"
              >
                <PackageCheck className="h-5 w-5 text-white" />
                <span className="hidden sm:block text-[11px] font-bold leading-tight mt-1">
                  {currentUser.role === 'Staff' ? 'Xử lý đơn' : 'Quản trị'}
                </span>
              </button>
            )}

            {/* 5. Tài Khoản */}
            {currentUser ? (
              <div 
                onClick={handleOpenProfile}
                className="flex flex-col items-center justify-center px-1.5 sm:px-2.5 py-1.5 rounded-xl cursor-pointer hover:bg-white/10 transition"
              >
                <div className="w-5 h-5 rounded-full bg-white text-blue-600 flex items-center justify-center text-[10px] font-black">
                  {currentUser.fullName ? currentUser.fullName.charAt(0).toUpperCase() : 'U'}
                </div>
                <span className="hidden sm:block text-[11px] font-bold mt-1 text-center max-w-[70px] truncate leading-tight">
                  {currentUser.fullName}
                </span>
              </div>
            ) : activeTab === 'orders' ? (
              <section className="mx-auto max-w-6xl space-y-5">
                {orderUpdateNotice && (
                  <div role="status" aria-live="polite" className="rounded-xl border border-blue-200 bg-blue-50 p-3 text-xs font-semibold text-blue-800">
                    {orderUpdateNotice}
                  </div>
                )}
                {selectedUserOrder ? (
                  <>
                    <button
                      type="button"
                      onClick={() => {
                        setSelectedUserOrder(null);
                        setOrderDetailError('');
                      }}
                      className="text-xs font-bold text-blue-600 hover:text-blue-700"
                    >
                      ← Quay lại danh sách đơn hàng
                    </button>
                    {orderDetailLoading ? (
                      <div className="flex justify-center rounded-2xl border border-slate-200 bg-white p-12">
                        <Loader2 className="h-6 w-6 animate-spin text-blue-600" />
                      </div>
                    ) : orderDetailError ? (
                      <div role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-xs text-red-700">
                        {orderDetailError}
                      </div>
                    ) : (
                      <>
                        <div className="flex flex-col justify-between gap-3 rounded-2xl border border-slate-200 bg-white p-5 sm:flex-row sm:items-center">
                          <div>
                            <p className="text-[10px] font-bold uppercase text-slate-500">Chi tiết đơn hàng</p>
                            <h1 className="mt-1 text-lg font-black text-slate-900">{selectedUserOrder.orderCode}</h1>
                            <p className="mt-1 text-xs text-slate-500">
                              Đặt ngày {new Date(selectedUserOrder.createdAt).toLocaleString('vi-VN')}
                            </p>
                          </div>
                          <span className="w-fit rounded-full bg-blue-50 px-3 py-1.5 text-xs font-bold text-blue-700">
                            {({
                              Pending: 'Chờ xử lý',
                              Confirmed: 'Đã xác nhận / Đang đóng gói',
                              Shipping: 'Đã bàn giao vận chuyển / Đang giao hàng',
                              Completed: 'Giao thành công',
                              Cancelled: 'Đã hủy',
                              DeliveryFailed: 'Giao hàng thất bại',
                              Refunded: 'Đã hoàn tiền',
                            })[selectedUserOrder.status] || selectedUserOrder.status}
                          </span>
                        </div>

                        <div className="grid grid-cols-1 gap-5 lg:grid-cols-3">
                          <div className="space-y-5 lg:col-span-2">
                            <div className="rounded-2xl border border-slate-200 bg-white p-5">
                              <h2 className="text-sm font-black uppercase text-slate-900">Tiến trình đơn hàng</h2>
                              {selectedUserOrder.status === 'Cancelled' || selectedUserOrder.status === 'DeliveryFailed' || selectedUserOrder.status === 'Refunded' ? (
                                <div className="mt-4 rounded-xl border border-amber-200 bg-amber-50 p-4 text-xs font-bold text-amber-800">
                                  {selectedUserOrder.status === 'Cancelled'
                                    ? 'Đơn hàng đã bị hủy.'
                                    : selectedUserOrder.status === 'DeliveryFailed'
                                      ? 'Giao hàng không thành công. Vui lòng liên hệ cửa hàng để được hỗ trợ.'
                                      : 'Đơn hàng đã được hoàn tiền.'}
                                  {selectedUserOrder.statusHistory?.find(entry => entry.status === selectedUserOrder.status)?.changedAt && (
                                    <p className="mt-1 font-normal">
                                      {new Date(selectedUserOrder.statusHistory.find(entry => entry.status === selectedUserOrder.status).changedAt).toLocaleString('vi-VN')}
                                    </p>
                                  )}
                                </div>
                              ) : (
                                <ol className="mt-5 space-y-0">
                                  {[
                                    { status: 'Pending', label: 'Chờ xác nhận', icon: Clock },
                                    { status: 'Confirmed', label: 'Đã xác nhận / Đang đóng gói', icon: Box },
                                    { status: 'Shipping', label: 'Đã bàn giao vận chuyển / Đang giao hàng', icon: Truck },
                                    { status: 'Completed', label: 'Giao thành công', icon: CheckCircle2 },
                                  ].map((step, index, steps) => {
                                    const history = selectedUserOrder.statusHistory?.find(entry => entry.status === step.status);
                                    const stepIndex = steps.findIndex(item => item.status === selectedUserOrder.status);
                                    const isCurrent = step.status === selectedUserOrder.status;
                                    const isComplete = (stepIndex >= 0 && index < stepIndex) || Boolean(history && !isCurrent);
                                    const StepIcon = step.icon;
                                    return (
                                      <li key={step.status} className="relative flex gap-3 pb-6 last:pb-0">
                                        {index < steps.length - 1 && (
                                          <span className={`absolute left-[13px] top-7 h-[calc(100%-1rem)] w-0.5 ${
                                            isComplete ? 'bg-emerald-400' : 'bg-slate-200'
                                          }`} />
                                        )}
                                        <span className={`relative z-10 flex h-7 w-7 shrink-0 items-center justify-center rounded-full ${
                                          isCurrent || isComplete ? 'bg-emerald-100 text-emerald-700' : 'bg-slate-100 text-slate-400'
                                        }`}>
                                          <StepIcon className="h-4 w-4" />
                                        </span>
                                        <div className="min-w-0 pt-0.5">
                                          <p className={`text-xs font-bold ${isCurrent ? 'text-emerald-800' : 'text-slate-700'}`}>
                                            {step.label}{isCurrent ? ' · Hiện tại' : ''}
                                          </p>
                                          {history && (
                                            <p className="mt-1 text-[11px] text-slate-500">
                                              {new Date(history.changedAt).toLocaleString('vi-VN')}
                                            </p>
                                          )}
                                          {history?.trackingNumber && (
                                            <p className="mt-1 text-[11px] font-semibold text-blue-700">
                                              {history.carrier && `${history.carrier} · `}Mã vận đơn: {history.trackingNumber}
                                            </p>
                                          )}
                                          {history?.changedByName && <p className="mt-1 text-[10px] text-slate-500">Cập nhật bởi: {history.changedByName}</p>}
                                          {history?.note && <p className="mt-1 text-[11px] text-slate-600">Ghi chú: {history.note}</p>}
                                        </div>
                                      </li>
                                    );
                                  })}
                                </ol>
                              )}
                              {selectedUserOrder.status === 'Shipping' && !selectedUserOrder.statusHistory?.some(entry => entry.trackingNumber) && (
                                <p className="mt-2 text-[11px] text-slate-500">Mã vận đơn sẽ hiển thị tại đây khi cửa hàng cập nhật.</p>
                              )}
                              {(!selectedUserOrder.statusHistory || selectedUserOrder.statusHistory.length === 0) && (
                                <p className="mt-3 text-[11px] text-slate-500">Đơn hàng được tạo trước khi lưu lịch sử tiến trình; các mốc thời gian chi tiết chưa có.</p>
                              )}
                            </div>

                            <div className="rounded-2xl border border-slate-200 bg-white p-5">
                              <h2 className="text-sm font-black uppercase text-slate-900">Sản phẩm đã mua</h2>
                              <div className="mt-4 divide-y divide-slate-100">
                                {selectedUserOrder.items?.map(item => {
                                  const reviewKey = `${selectedUserOrder.id}:${item.productId}`;
                                  const draft = reviewDrafts[reviewKey] || { rating: 5, comment: '', imageUrl: '', error: '' };
                                  return (
                                    <div key={item.productId} className="py-3 first:pt-0 last:pb-0">
                                      <div className="flex items-center justify-between gap-4">
                                        <div className="flex min-w-0 items-center gap-3">
                                          <div className="flex h-12 w-12 shrink-0 items-center justify-center overflow-hidden rounded-lg bg-slate-50">
                                            {item.imageUrl
                                              ? <img src={item.imageUrl} alt={item.productName} className="h-full w-full object-contain p-1" />
                                              : <PackageCheck className="h-5 w-5 text-slate-400" />}
                                          </div>
                                          <div className="min-w-0">
                                            <p className="truncate text-xs font-bold text-slate-800">{item.productName}</p>
                                            <p className="mt-1 text-[11px] text-slate-500">Số lượng: {item.quantity}</p>
                                          </div>
                                        </div>
                                        <p className="shrink-0 text-xs font-bold text-slate-800">
                                          {(Number(item.unitPrice) * item.quantity).toLocaleString('vi-VN')} đ
                                        </p>
                                      </div>

                                      {selectedUserOrder.status === 'Completed' && item.hasReviewed ? (
                                        <p className="mt-2 text-[11px] font-semibold text-emerald-700">✓ Bạn đã đánh giá sản phẩm này</p>
                                      ) : selectedUserOrder.status === 'Completed' ? (
                                        <div className="mt-3 rounded-xl border border-slate-200 bg-slate-50 p-3">
                                          <p className="text-[11px] font-bold text-slate-700">Đánh giá sản phẩm</p>
                                          <div className="mt-2 flex items-center gap-1" role="group" aria-label="Chọn số sao đánh giá">
                                            {[1, 2, 3, 4, 5].map(star => (
                                              <button
                                                key={star}
                                                type="button"
                                                onClick={() => setReviewDrafts(prev => ({ ...prev, [reviewKey]: { ...draft, rating: star } }))}
                                                aria-label={`${star} sao`}
                                                className="p-0.5"
                                              >
                                                <Star className={`h-5 w-5 ${star <= draft.rating ? 'fill-amber-400 text-amber-400' : 'text-slate-300'}`} />
                                              </button>
                                            ))}
                                          </div>
                                          <textarea
                                            value={draft.comment}
                                            onChange={event => setReviewDrafts(prev => ({ ...prev, [reviewKey]: { ...draft, comment: event.target.value, error: '' } }))}
                                            maxLength={1000}
                                            rows={3}
                                            placeholder="Chia sẻ nhận xét của bạn (không bắt buộc)"
                                            className="mt-2 w-full resize-y rounded-lg border border-slate-200 bg-white p-2 text-xs outline-none focus:border-blue-400"
                                          />
                                          <div className="mt-2 flex flex-wrap items-center gap-3">
                                            <label className="cursor-pointer rounded-lg border border-slate-200 bg-white px-3 py-2 text-[11px] font-semibold text-slate-700 hover:bg-slate-100">
                                              Chọn ảnh (tùy chọn)
                                              <input
                                                type="file"
                                                accept="image/jpeg,image/png,image/webp"
                                                className="sr-only"
                                                onChange={event => handleReviewImageChange(selectedUserOrder.id, item.productId, event.target.files?.[0])}
                                              />
                                            </label>
                                            {draft.imageUrl && <span className="text-[11px] text-emerald-700">Đã chọn ảnh</span>}
                                            <button
                                              type="button"
                                              onClick={() => handleSubmitProductReview(selectedUserOrder.id, item.productId)}
                                              disabled={reviewSubmittingKey === reviewKey}
                                              className="ml-auto rounded-lg bg-blue-600 px-4 py-2 text-[11px] font-bold text-white hover:bg-blue-700 disabled:opacity-50"
                                            >
                                              {reviewSubmittingKey === reviewKey ? 'Đang gửi...' : 'Gửi đánh giá'}
                                            </button>
                                          </div>
                                          {draft.error && <p role="alert" className="mt-2 text-[11px] text-red-600">{draft.error}</p>}
                                        </div>
                                      ) : null}
                                    </div>
                                  );
                                })}
                              </div>
                            </div>
                          </div>

                          <aside className="h-fit space-y-4 rounded-2xl border border-slate-200 bg-white p-5">
                            <h2 className="text-sm font-black uppercase text-slate-900">Thông tin đơn hàng</h2>
                            <div className="space-y-3 text-xs">
                              <div>
                                <p className="text-slate-500">Người nhận</p>
                                <p className="mt-1 font-semibold text-slate-800">{selectedUserOrder.receiverName}</p>
                              </div>
                              <div>
                                <p className="text-slate-500">Số điện thoại</p>
                                <p className="mt-1 font-semibold text-slate-800">{selectedUserOrder.receiverPhone}</p>
                              </div>
                              <div>
                                <p className="text-slate-500">Địa chỉ giao hàng</p>
                                <p className="mt-1 font-semibold text-slate-800">{selectedUserOrder.shippingAddress}</p>
                              </div>
                              <div>
                                <p className="text-slate-500">Thanh toán</p>
                                <p className="mt-1 font-semibold text-slate-800">
                                  {selectedUserOrder.paymentMethod === 'COD' ? 'Thanh toán khi nhận hàng (COD)' : selectedUserOrder.paymentMethod}
                                </p>
                              </div>
                              <div className="flex justify-between border-t border-slate-100 pt-3 font-black">
                                <span>Tổng tiền</span>
                                <span className="text-red-600">{Number(selectedUserOrder.totalAmount).toLocaleString('vi-VN')} đ</span>
                              </div>
                            </div>
                          </aside>
                        </div>
                      </>
                    )}
                  </>
                ) : (
                  <>
                    <div className="flex items-end justify-between gap-3">
                      <div>
                        <h1 className="text-xl font-black text-slate-900">Đơn hàng của tôi</h1>
                        <p className="mt-1 text-xs text-slate-500">Theo dõi trạng thái và lịch sử đơn hàng.</p>
                      </div>
                      <button
                        type="button"
                        onClick={handleOpenUserOrders}
                        disabled={ordersLoading}
                        className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs font-bold text-slate-700 hover:bg-slate-50 disabled:opacity-50"
                      >
                        Làm mới
                      </button>
                    </div>
                    {ordersLoading || orderDetailLoading ? (
                      <div className="flex justify-center rounded-2xl border border-slate-200 bg-white p-12">
                        <Loader2 className="h-6 w-6 animate-spin text-blue-600" />
                      </div>
                    ) : ordersError || orderDetailError ? (
                      <div role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-xs text-red-700">
                        {ordersError || orderDetailError}
                      </div>
                    ) : userOrders.length === 0 ? (
                      <div className="rounded-2xl border border-slate-200 bg-white py-14 text-center">
                        <ClipboardList className="mx-auto h-10 w-10 text-slate-300" />
                        <h2 className="mt-3 text-sm font-black text-slate-800">Bạn chưa có đơn hàng nào</h2>
                        <button type="button" onClick={() => setActiveTab('home')} className="mt-4 text-xs font-bold text-blue-600 underline">
                          Tiếp tục mua sắm
                        </button>
                      </div>
                    ) : (
                      <div className="space-y-3">
                        {userOrders.map(order => (
                          <article key={order.id} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
                            <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-center">
                              <div>
                                <h2 className="text-sm font-black text-slate-900">{order.orderCode}</h2>
                                <p className="mt-1 text-[11px] text-slate-500">
                                  Đặt ngày {new Date(order.createdAt).toLocaleString('vi-VN')} · {order.itemCount} sản phẩm
                                </p>
                                {order.trackingNumber && (
                                  <p className="mt-1 text-[11px] font-semibold text-blue-700">{order.carrier && `${order.carrier} · `}Mã vận đơn: {order.trackingNumber}</p>
                                )}
                              </div>
                              <span className="w-fit rounded-full bg-blue-50 px-3 py-1.5 text-[11px] font-bold text-blue-700">
                                {({
                                  Pending: 'Chờ xử lý',
                                  Confirmed: 'Đã xác nhận / Đang đóng gói',
                                  Shipping: 'Đã bàn giao vận chuyển / Đang giao hàng',
                                  Completed: 'Giao thành công',
                                  Cancelled: 'Đã hủy',
                                  DeliveryFailed: 'Giao hàng thất bại',
                                  Refunded: 'Đã hoàn tiền',
                                })[order.status] || order.status}
                              </span>
                            </div>
                            <div className="mt-4 flex flex-col justify-between gap-3 border-t border-slate-100 pt-3 sm:flex-row sm:items-center">
                              <div className="min-w-0 space-y-1">
                                {order.items?.slice(0, 2).map(item => (
                                  <p key={item.productId} className="truncate text-xs text-slate-600">
                                    {item.productName} × {item.quantity}
                                  </p>
                                ))}
                                {order.items?.length > 2 && <p className="text-[11px] text-slate-400">và {order.items.length - 2} sản phẩm khác</p>}
                              </div>
                              <div className="flex shrink-0 items-center justify-between gap-4 sm:justify-end">
                                <p className="text-sm font-black text-red-600">{Number(order.totalAmount).toLocaleString('vi-VN')} đ</p>
                                <button
                                  type="button"
                                  onClick={() => handleOpenUserOrder(order.id)}
                                  className="rounded-lg bg-blue-600 px-3 py-2 text-[11px] font-bold text-white hover:bg-blue-700"
                                >
                                  Xem chi tiết
                                </button>
                              </div>
                            </div>
                          </article>
                        ))}
                      </div>
                    )}
                  </>
                )}
              </section>
            ) : (
              <div 
                onClick={() => { setAuthTab('signin'); setIsAuthOpen(true); }}
                className="flex flex-col items-center justify-center px-1.5 sm:px-2.5 py-1.5 rounded-xl cursor-pointer hover:bg-white/10 transition"
              >
                <User className="w-5 h-5 text-white" />
                <span className="hidden sm:block text-[11px] font-bold mt-1 text-center whitespace-nowrap leading-tight">
                  Tài Khoản
                </span>
              </div>
            )}

          </div>

        </div>
      </header>

      <nav aria-label="Danh mục sản phẩm" className="bg-white border-b border-slate-200 shadow-sm">
        <div className="max-w-7xl mx-auto px-4 min-h-11 flex items-center gap-1 overflow-x-auto whitespace-nowrap">
          <button
            onClick={() => setActiveTab('home')}
            className="flex items-center gap-2 shrink-0 px-3 py-3 text-[11px] font-black uppercase text-white bg-[#d71920]"
          >
            <Layers className="w-4 h-4" /> Danh mục sản phẩm
          </button>
          {categories.map((category) => (
            <button
              key={category.id}
              onClick={() => {
                setActiveTab('home');
                setSelectedCategoryTab(category.type);
                document.getElementById('catalog-section')?.scrollIntoView({ behavior: 'smooth' });
              }}
              className="shrink-0 px-3 py-3 text-[11px] font-bold uppercase text-slate-700 hover:text-[#d71920] transition"
            >
              {category.name}
            </button>
          ))}
        </div>
      </nav>

      {/* 3. NỘI DUNG CHÍNH (MAIN) */}
      <main className="flex-1 max-w-7xl mx-auto px-4 w-full py-5">
        
        {activeTab === 'home' ? (
          <div className="space-y-6">
            
            {/* HERO SECTION: MEGA MENU + SLIDER BANNER (Form chuẩn An Phát / Nguyễn Công) */}
            <div className="grid grid-cols-1 lg:grid-cols-12 gap-3">
              
              {/* Cột Mega Menu Trái (3 Cột) */}
              <div className="hidden lg:block lg:col-span-3 bg-white rounded-md border border-slate-200 shadow-sm p-3 space-y-1">
                <div className="px-3 py-2 text-xs font-black uppercase text-slate-800 border-b border-slate-100 flex items-center gap-2">
                  <Layers className="w-4 h-4 text-blue-600" /> Danh Mục Linh Kiện
                </div>
                
                <div
                  onClick={() => setSelectedCategoryTab('ALL')}
                  className={`flex items-center justify-between px-3 py-2.5 rounded-xl text-xs font-bold cursor-pointer transition ${
                    selectedCategoryTab === 'ALL' ? 'bg-blue-50 text-blue-600' : 'hover:bg-slate-50 text-slate-700'
                  }`}
                >
                  <div className="flex items-center gap-2.5">
                    <Compass className="w-4 h-4 text-slate-400" />
                    <span>Tất Cả Linh Kiện</span>
                  </div>
                  <ChevronRight className="w-3.5 h-3.5 text-slate-400" />
                </div>

                {categories.map((c) => (
                  <div
                    key={c.id}
                    onClick={() => {
                      setSelectedCategoryTab(c.type);
                      document.getElementById('catalog-section')?.scrollIntoView({ behavior: 'smooth' });
                    }}
                    className={`flex items-center justify-between px-3 py-2.5 rounded-xl text-xs font-bold cursor-pointer transition ${
                      selectedCategoryTab === c.type ? 'bg-blue-50 text-blue-600' : 'hover:bg-slate-50 text-slate-700'
                    }`}
                  >
                    <div className="flex items-center gap-2.5">
                      {renderIcon(c.type)}
                      <span className="truncate">{c.name}</span>
                    </div>
                    <ChevronRight className="w-3.5 h-3.5 text-slate-400" />
                  </div>
                ))}
              </div>

              {/* Khung Banner Chính + Banner Phụ Phải (9 Cột) */}
              <div className="lg:col-span-9 space-y-4">
                
                {/* Banner Quảng Cáo Lớn */}
                <div className="rounded-md bg-[#b5121b] p-6 md:p-8 text-white shadow-md relative overflow-hidden flex flex-col justify-between min-h-[260px]">
                  <div className="space-y-2 max-w-lg z-10">
                      <span className="inline-flex items-center gap-1.5 px-3 py-0.5 rounded-sm bg-amber-400 text-slate-900 text-[10px] font-black uppercase">
                      <Flame className="w-3 h-3 text-red-600" /> Lễ Hội Linh Kiện PC 2026
                    </span>
                    <h2 className="text-2xl md:text-3xl font-black text-white leading-tight">
                      Giá tốt mỗi ngày cho dàn PC của bạn. <br />
                      <span className="text-amber-300">Linh kiện chính hãng, build chuẩn nhu cầu.</span>
                    </h2>
                    <p className="text-xs text-red-100 max-w-md">
                      Chọn linh kiện phù hợp, kiểm tra tương thích và hoàn thiện cấu hình trong một nơi.
                    </p>
                  </div>

                  <div className="flex gap-3 pt-4 z-10">
                    <button 
                      onClick={() => setActiveTab('builder')}
                      className="px-3 sm:px-5 py-2.5 bg-amber-400 hover:bg-amber-300 text-slate-900 font-black text-xs rounded-md shadow-md transition flex items-center justify-center gap-1.5 whitespace-nowrap"
                    >
                      <Wrench className="w-4 h-4" /> <span className="sm:hidden">Ráp PC</span><span className="hidden sm:inline">Bắt Đầu Ráp PC</span>
                    </button>
                    <button 
                      onClick={() => setActiveTab('ai')}
                      className="px-3 sm:px-5 py-2.5 bg-white/15 hover:bg-white/25 text-white font-bold text-xs rounded-md border border-white/30 transition flex items-center justify-center gap-1.5 whitespace-nowrap"
                    >
                      <Sparkles className="w-4 h-4 text-amber-300" /> <span className="sm:hidden">Gợi ý AI</span><span className="hidden sm:inline">Nhờ AI Đề Xuất</span>
                    </button>
                  </div>
                </div>

                {/* 3 Sub Banners Nhỏ */}
                <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
                  <div className="bg-white p-3.5 rounded-md border border-slate-200 flex items-center gap-3 shadow-2xs">
                    <div className="p-2.5 rounded-xl bg-blue-50 text-blue-600">
                      <Truck className="w-5 h-5" />
                    </div>
                    <div>
                      <h4 className="text-xs font-black text-slate-800">Miễn Phí Vận Chuyển</h4>
                      <p className="text-[11px] text-slate-500">Đơn hàng PC nguyên bộ</p>
                    </div>
                  </div>
                  
                  <div className="bg-white p-3.5 rounded-md border border-slate-200 flex items-center gap-3 shadow-2xs">
                    <div className="p-2.5 rounded-xl bg-emerald-50 text-emerald-600">
                      <RotateCcw className="w-5 h-5" />
                    </div>
                    <div>
                      <h4 className="text-xs font-black text-slate-800">1 Đổi 1 Trong 30 Ngày</h4>
                      <p className="text-[11px] text-slate-500">Nếu lỗi phần cứng từ NSX</p>
                    </div>
                  </div>

                  <div className="bg-white p-3.5 rounded-md border border-slate-200 flex items-center gap-3 shadow-2xs">
                    <div className="p-2.5 rounded-xl bg-amber-50 text-amber-600">
                      <CreditCard className="w-5 h-5" />
                    </div>
                    <div>
                      <h4 className="text-xs font-black text-slate-800">Trả Góp 0% Lãi Suất</h4>
                      <p className="text-[11px] text-slate-500">Thủ tục duyệt nhanh trong 15p</p>
                    </div>
                  </div>
                </div>

              </div>

            </div>

            {/* DANH SÁCH SẢN PHẨM & BỘ LỌC */}
            <section id="catalog-section" className="space-y-4">
              {apiError && (
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 bg-red-50 border border-red-200 rounded-md p-4">
                  <div className="flex items-start gap-2.5">
                    <AlertTriangle className="w-4 h-4 text-red-600 shrink-0 mt-0.5" />
                    <div>
                      <p className="text-xs font-bold text-red-900">Chưa kết nối được kho sản phẩm</p>
                      <p className="text-[11px] text-red-700">{apiError}</p>
                    </div>
                  </div>
                  <button onClick={fetchProducts} className="shrink-0 px-4 py-2 bg-[#d71920] hover:bg-red-800 text-white font-bold text-xs rounded-md transition">
                    Thử lại
                  </button>
                </div>
              )}
              
              {/* ===== BỘ LỌC THÔNG MINH THEO DANH MỤC ===== */}
              {(() => {
                // Lấy unique values từ allProducts để tạo option filter
                const uniqueBrands = [...new Set(allProducts.map(p => p.brand).filter(Boolean))].sort();
                const uniqueSockets = [...new Set(allProducts.map(p => p.socket).filter(Boolean))].sort();
                const uniqueChipsets = [...new Set(allProducts.map(p => p.chipset).filter(Boolean))].sort();
                const uniqueRamTypes = [...new Set(allProducts.map(p => p.ramType).filter(Boolean))].sort();
                const uniqueFormFactors = [...new Set(allProducts.map(p => p.formFactor).filter(Boolean))].sort();

                // Đếm sản phẩm cho từng brand
                const brandCount = {};
                allProducts.forEach(p => { if (p.brand) brandCount[p.brand] = (brandCount[p.brand] || 0) + 1; });

                // Đếm sản phẩm cho từng khoảng giá
                const priceRanges = [
                  { key: 'UNDER_2M', label: 'Dưới 2 triệu', min: 0, max: 2000000 },
                  { key: '2M_5M', label: '2 - 5 triệu', min: 2000000, max: 5000000 },
                  { key: '5M_10M', label: '5 - 10 triệu', min: 5000000, max: 10000000 },
                  { key: '10M_20M', label: '10 - 20 triệu', min: 10000000, max: 20000000 },
                  { key: 'OVER_20M', label: 'Trên 20 triệu', min: 20000000, max: Infinity },
                ];
                const priceCount = {};
                priceRanges.forEach(r => {
                  priceCount[r.key] = allProducts.filter(p => (p.price || 0) >= r.min && (p.price || 0) < r.max).length;
                });

                // Kiểm tra có filter nào đang active không
                const hasActiveFilter = brandFilter !== 'ALL' || socketFilter !== 'ALL' || coreFilter !== 'ALL'
                  || chipsetFilter !== 'ALL' || ramTypeFilter !== 'ALL' || formFactorFilter !== 'ALL'
                  || vramFilter !== 'ALL' || wattageFilter !== 'ALL' || priceRange !== 'ALL';

                // Brand logo/icon map
                const brandStyle = {
                  'Intel': { bg: 'bg-blue-50', text: 'text-blue-700', border: 'border-blue-200', activeBg: 'bg-blue-600', activeText: 'text-white' },
                  'AMD': { bg: 'bg-red-50', text: 'text-red-700', border: 'border-red-200', activeBg: 'bg-red-600', activeText: 'text-white' },
                  'ASUS': { bg: 'bg-blue-50', text: 'text-blue-700', border: 'border-blue-200', activeBg: 'bg-blue-600', activeText: 'text-white' },
                  'MSI': { bg: 'bg-red-50', text: 'text-red-700', border: 'border-red-200', activeBg: 'bg-red-600', activeText: 'text-white' },
                  'Gigabyte': { bg: 'bg-orange-50', text: 'text-orange-700', border: 'border-orange-200', activeBg: 'bg-orange-500', activeText: 'text-white' },
                  'ASRock': { bg: 'bg-slate-50', text: 'text-slate-700', border: 'border-slate-200', activeBg: 'bg-slate-600', activeText: 'text-white' },
                  'Kingston': { bg: 'bg-emerald-50', text: 'text-emerald-700', border: 'border-emerald-200', activeBg: 'bg-emerald-600', activeText: 'text-white' },
                  'Corsair': { bg: 'bg-yellow-50', text: 'text-yellow-700', border: 'border-yellow-200', activeBg: 'bg-yellow-600', activeText: 'text-white' },
                  'Samsung': { bg: 'bg-blue-50', text: 'text-blue-700', border: 'border-blue-200', activeBg: 'bg-blue-600', activeText: 'text-white' },
                  'NVIDIA': { bg: 'bg-green-50', text: 'text-green-700', border: 'border-green-200', activeBg: 'bg-green-600', activeText: 'text-white' },
                };
                const defaultStyle = { bg: 'bg-slate-50', text: 'text-slate-700', border: 'border-slate-200', activeBg: 'bg-slate-700', activeText: 'text-white' };

                // Filter tiêu chí theo danh mục
                const getCriteriaFilters = () => {
                  const cat = selectedCategoryTab;
                  const filters = [];

                  if (cat === 'CPU') {
                    if (uniqueSockets.length > 0) filters.push(
                      <div key="socket" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Socket:</span>
                        <div className="flex flex-wrap gap-1">
                          {uniqueSockets.map(s => (
                            <button key={s} onClick={() => setSocketFilter(socketFilter === s ? 'ALL' : s)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${socketFilter === s ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {s}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                    filters.push(
                      <div key="cores" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Số nhân:</span>
                        <div className="flex flex-wrap gap-1">
                          {['2', '4', '6', '8', '12+', '16+'].map(c => (
                            <button key={c} onClick={() => setCoreFilter(coreFilter === c ? 'ALL' : c)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${coreFilter === c ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {c}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  }

                  if (cat === 'Mainboard') {
                    if (uniqueSockets.length > 0) filters.push(
                      <div key="socket" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Socket:</span>
                        <div className="flex flex-wrap gap-1">
                          {uniqueSockets.map(s => (
                            <button key={s} onClick={() => setSocketFilter(socketFilter === s ? 'ALL' : s)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${socketFilter === s ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {s}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                    if (uniqueChipsets.length > 0) filters.push(
                      <div key="chipset" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Chipset:</span>
                        <div className="flex flex-wrap gap-1">
                          {uniqueChipsets.map(c => (
                            <button key={c} onClick={() => setChipsetFilter(chipsetFilter === c ? 'ALL' : c)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${chipsetFilter === c ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {c}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                    if (uniqueFormFactors.length > 0) filters.push(
                      <div key="form" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Form factor:</span>
                        <div className="flex flex-wrap gap-1">
                          {uniqueFormFactors.map(f => (
                            <button key={f} onClick={() => setFormFactorFilter(formFactorFilter === f ? 'ALL' : f)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${formFactorFilter === f ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {f}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                    if (uniqueRamTypes.length > 0) filters.push(
                      <div key="ram" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">RAM:</span>
                        <div className="flex flex-wrap gap-1">
                          {['DDR4', 'DDR5'].map(r => (
                            <button key={r} onClick={() => setRamTypeFilter(ramTypeFilter === r ? 'ALL' : r)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${ramTypeFilter === r ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {r}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  }

                  if (cat === 'RAM') {
                    filters.push(
                      <div key="ramtype" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Chuẩn RAM:</span>
                        <div className="flex flex-wrap gap-1">
                          {['DDR4', 'DDR5'].map(r => (
                            <button key={r} onClick={() => setRamTypeFilter(ramTypeFilter === r ? 'ALL' : r)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${ramTypeFilter === r ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {r}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                    filters.push(
                      <div key="ramcap" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Dung lượng:</span>
                        <div className="flex flex-wrap gap-1">
                          {['8GB', '16GB', '32GB', '64GB'].map(s => (
                            <button key={s} onClick={() => setVramFilter(vramFilter === s ? 'ALL' : s)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${vramFilter === s ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {s}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  }

                  if (cat === 'GPU') {
                    filters.push(
                      <div key="vram" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">VRAM:</span>
                        <div className="flex flex-wrap gap-1">
                          {['8GB', '12GB', '16GB', '24GB'].map(s => (
                            <button key={s} onClick={() => setVramFilter(vramFilter === s ? 'ALL' : s)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${vramFilter === s ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {s}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  }

                  if (cat === 'PSU') {
                    filters.push(
                      <div key="watt" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Công suất:</span>
                        <div className="flex flex-wrap gap-1">
                          {[
                            { key: 'UNDER_500', label: 'Dưới 500W' },
                            { key: '500_650', label: '500 - 650W' },
                            { key: '650_850', label: '650 - 850W' },
                            { key: 'OVER_850', label: 'Trên 850W' },
                          ].map(w => (
                            <button key={w.key} onClick={() => setWattageFilter(wattageFilter === w.key ? 'ALL' : w.key)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${wattageFilter === w.key ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {w.label}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  }

                  if (cat === 'SSD') {
                    filters.push(
                      <div key="ssdcap" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Dung lượng:</span>
                        <div className="flex flex-wrap gap-1">
                          {['256GB', '512GB', '1TB', '2TB', '4TB'].map(s => (
                            <button key={s} onClick={() => setVramFilter(vramFilter === s ? 'ALL' : s)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${vramFilter === s ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {s}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  }

                  if (cat === 'Case') {
                    filters.push(
                      <div key="ff" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Form factor:</span>
                        <div className="flex flex-wrap gap-1">
                          {['ATX', 'Micro-ATX', 'Mini-ITX', 'E-ATX'].map(f => (
                            <button key={f} onClick={() => setFormFactorFilter(formFactorFilter === f ? 'ALL' : f)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${formFactorFilter === f ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {f}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  }

                  if (cat === 'Cooler') {
                    filters.push(
                      <div key="coolertype" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Loại tản:</span>
                        <div className="flex flex-wrap gap-1">
                          {['Air', 'AIO', '240mm', '280mm', '360mm'].map(t => (
                            <button key={t} onClick={() => setVramFilter(vramFilter === t ? 'ALL' : t)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${vramFilter === t ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {t}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                    if (uniqueSockets.length > 0) filters.push(
                      <div key="socket" className="flex items-center gap-1.5">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Socket:</span>
                        <div className="flex flex-wrap gap-1">
                          {uniqueSockets.map(s => (
                            <button key={s} onClick={() => setSocketFilter(socketFilter === s ? 'ALL' : s)}
                              className={`px-2.5 py-1 rounded-full text-[11px] font-bold border transition ${socketFilter === s ? 'bg-blue-600 text-white border-blue-600' : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'}`}>
                              {s}
                            </button>
                          ))}
                        </div>
                      </div>
                    );
                  }

                  return filters;
                };

                const criteriaFilters = getCriteriaFilters();

                return (
                  <div className="bg-white rounded-md border border-slate-200 shadow-2xs overflow-hidden">
                    {/* Tiêu đề danh mục */}
                    <div className="px-4 pt-4 pb-2 border-b border-slate-100">
                      <div className="flex items-center justify-between">
                        <div>
                          <h3 className="text-base font-black text-slate-900">
                            {selectedCategoryTab === 'ALL'
                              ? 'Tất Cả Linh Kiện'
                              : (categories.find(c => c.type === selectedCategoryTab)?.name || selectedCategoryTab)}
                            <span className="ml-2 text-sm font-bold text-slate-400">({products.length} sản phẩm)</span>
                          </h3>
                        </div>
                        {hasActiveFilter && (
                          <button
                            onClick={() => { resetCategoryFilters(); setPriceRange('ALL'); }}
                            className="flex items-center gap-1.5 text-[11px] font-bold text-red-600 hover:text-red-700 transition"
                          >
                            <X className="w-3.5 h-3.5" /> Bỏ tất cả lọc
                          </button>
                        )}
                      </div>
                    </div>

                    {/* Brand Shortcuts */}
                    {uniqueBrands.length > 0 && (
                      <div className="px-4 py-3 border-b border-slate-100 flex flex-wrap gap-2">
                        {uniqueBrands.map(b => {
                          const style = brandStyle[b] || defaultStyle;
                          const isActive = brandFilter === b;
                          return (
                            <button
                              key={b}
                              onClick={() => setBrandFilter(isActive ? 'ALL' : b)}
                              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-xl border text-xs font-bold transition shadow-xs ${
                                isActive
                                  ? `${style.activeBg} ${style.activeText} border-transparent`
                                  : `${style.bg} ${style.text} ${style.border} hover:shadow-sm`
                              }`}
                            >
                              <span>{b}</span>
                              <span className={`text-[10px] px-1.5 py-0.5 rounded-full ${isActive ? 'bg-white/20' : 'bg-white/70'}`}>
                                {brandCount[b] || 0}
                              </span>
                            </button>
                          );
                        })}
                      </div>
                    )}

                    {/* Khoảng giá */}
                    <div className="px-4 py-3 border-b border-slate-100">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="text-[11px] font-bold text-slate-500 shrink-0">Khoảng giá:</span>
                        <div className="flex flex-wrap gap-1.5">
                          {priceRanges.filter(r => priceCount[r.key] > 0).map(r => (
                            <button
                              key={r.key}
                              onClick={() => setPriceRange(priceRange === r.key ? 'ALL' : r.key)}
                              className={`px-3 py-1 rounded-full text-[11px] font-bold border transition ${
                                priceRange === r.key
                                  ? 'bg-red-600 text-white border-red-600'
                                  : 'bg-white text-slate-600 border-slate-200 hover:border-red-400 hover:text-red-600'
                              }`}
                            >
                              {r.label} ({priceCount[r.key]})
                            </button>
                          ))}
                        </div>
                      </div>
                    </div>

                    {/* Tiêu chí lọc theo danh mục */}
                    {criteriaFilters.length > 0 && (
                      <div className="px-4 py-3 border-b border-slate-100 space-y-2.5">
                        <span className="text-[11px] font-bold text-slate-500 block">Chọn theo tiêu chí:</span>
                        <div className="flex flex-col gap-2">
                          {criteriaFilters}
                        </div>
                      </div>
                    )}

                    {/* Sort Bar */}
                    <div className="px-4 py-2.5 bg-slate-50 flex items-center justify-between gap-3 flex-wrap">
                      <div className="flex items-center gap-2 flex-wrap">
                        <span className="text-[11px] font-bold text-slate-500">Sắp xếp:</span>
                        {[
                          { key: 'default', icon: '⊞', label: 'Mặc định' },
                          { key: 'price_asc', icon: '↑', label: 'Giá tăng dần' },
                          { key: 'price_desc', icon: '↓', label: 'Giá giảm dần' },
                        ].map(s => (
                          <button
                            key={s.key}
                            onClick={() => setSortBy(s.key)}
                            className={`flex items-center gap-1 px-3 py-1.5 text-[11px] font-bold rounded-md border transition ${
                              sortBy === s.key
                                ? 'bg-blue-600 text-white border-blue-600'
                                : 'bg-white text-slate-600 border-slate-200 hover:border-blue-400'
                            }`}
                          >
                            <span>{s.icon}</span> {s.label}
                          </button>
                        ))}
                      </div>
                      <span className="text-[11px] text-slate-400 font-medium">
                        Hiển thị {products.length} / {allProducts.length} sản phẩm
                      </span>
                    </div>
                  </div>
                );
              })()}

              {/* Lưới sản phẩm */}
              {loading ? (
                <div className="py-20 text-center space-y-3 bg-white rounded-2xl border border-slate-200">
                  <Loader2 className="w-8 h-8 text-blue-600 animate-spin mx-auto" />
                  <p className="text-xs font-bold text-slate-500">Đang truy vấn danh sách linh kiện...</p>
                </div>
              ) : products.length === 0 ? (
                <div className="bg-white border border-slate-200 rounded-2xl p-12 text-center text-slate-400 space-y-2">
                  <Layers className="w-10 h-10 mx-auto text-slate-300" />
                  <p className="text-sm font-bold text-slate-600">Không tìm thấy linh kiện phù hợp với bộ lọc hiện tại.</p>
                </div>
              ) : (
                <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-3.5">
                  {products.map((p) => (
                    <div 
                      key={p.id} 
                      className="bg-white border border-slate-200 hover:border-[#d71920] rounded-md p-3 flex flex-col justify-between space-y-2.5 transition hover:shadow-lg shadow-2xs group relative"
                    >
                      {/* Badge Tiết Kiệm / Quà Tặng */}
                      <span className="absolute top-2 left-2 bg-red-600 text-white text-[9px] font-black px-1.5 py-0.5 rounded shadow-xs z-10">
                        TIẾT KIỆM
                      </span>

                      <div>
                        {/* Box Ảnh */}
                        <div 
                          onClick={() => handleOpenDetail(p.id)}
                          className="w-full h-36 bg-slate-50 rounded-xl flex items-center justify-center p-2 relative cursor-pointer overflow-hidden border border-slate-100"
                        >
                          {p.imageUrl && p.imageUrl.startsWith('http') ? (
                            <img src={p.imageUrl} alt={p.name} className="h-28 object-contain group-hover:scale-105 transition" />
                          ) : (
                            <div className="p-3 bg-white rounded-xl border border-slate-200 group-hover:scale-105 transition">
                              {renderIcon(p.categoryType)}
                            </div>
                          )}
                        </div>

                        {/* Thông tin sản phẩm */}
                        <div className="mt-2 space-y-1">
                          <div className="flex items-center justify-between text-[10px] text-slate-400">
                            <span className="font-bold uppercase text-blue-600">{p.brand}</span>
                            <span>BH: 36T</span>
                          </div>

                          <h4 
                            onClick={() => handleOpenDetail(p.id)}
                            className="text-xs font-bold text-slate-800 line-clamp-2 leading-snug cursor-pointer group-hover:text-blue-600 transition min-h-[32px]"
                            title={p.name}
                          >
                            {p.name}
                          </h4>

                          {/* Thông số kỹ thuật nhanh */}
                          <div className="flex gap-1 flex-wrap text-[9px] pt-1">
                            {p.socket && <span className="bg-blue-50 text-blue-700 px-1.5 py-0.5 rounded font-semibold">{p.socket}</span>}
                            {p.ramType && <span className="bg-sky-50 text-sky-700 px-1.5 py-0.5 rounded font-semibold">{p.ramType}</span>}
                            {p.tdpWattage > 0 && <span className="bg-slate-100 text-slate-600 px-1.5 py-0.5 rounded font-semibold">{p.tdpWattage}W</span>}
                          </div>
                        </div>
                      </div>

                      {/* Giá & Nút Chọn */}
                      <div className="space-y-2 pt-2 border-t border-slate-100">
                        <div>
                          <div className="text-sm font-black text-red-600">
                            {p.price?.toLocaleString('vi-VN')} đ
                          </div>
                          <div className="text-[10px] text-slate-400 line-through">
                            {((p.price || 0) * 1.08).toLocaleString('vi-VN')} đ
                          </div>
                        </div>

                        <div className="grid grid-cols-2 gap-1.5">
                          <button
                            onClick={() => {
                              handleSelectPart(p);
                              setActiveTab('builder');
                            }}
                            className="py-1.5 bg-blue-600 hover:bg-blue-700 text-white font-bold text-[11px] rounded-lg transition shadow-xs text-center"
                          >
                            Chọn Ráp
                          </button>
                          
                          <button
                            onClick={() => handleOpenDetail(p.id)}
                            className="py-1.5 bg-slate-100 hover:bg-slate-200 text-slate-700 font-bold text-[11px] rounded-lg transition text-center"
                          >
                            Chi Tiết
                          </button>
                        </div>
                        <button
                          onClick={() => handleAddProductToCart(p)}
                          disabled={(p.stockQuantity || 0) < 1}
                          className="w-full py-1.5 bg-amber-400 hover:bg-amber-300 disabled:bg-slate-200 disabled:text-slate-400 text-slate-900 font-bold text-[11px] rounded-lg transition flex items-center justify-center gap-1"
                        >
                          <ShoppingCart className="w-3.5 h-3.5" />
                          {(p.stockQuantity || 0) > 0 ? 'Thêm vào giỏ hàng' : 'Hết hàng'}
                        </button>

                        {/* Nút So sánh */}
                        {(() => {
                          const inCompare = compareList.find(c => c.id === p.id);
                          const wrongType = compareList.length > 0 && compareList[0].categoryType !== p.categoryType;
                          const isFull = compareList.length >= 3 && !inCompare;
                          return (
                            <button
                              onClick={() => handleToggleCompare(p)}
                              disabled={wrongType || isFull}
                              title={wrongType ? `Chỉ so sánh được sản phẩm cùng loại (${compareList[0]?.categoryType})` : isFull ? 'Đã chọn tối đa 3 sản phẩm' : inCompare ? 'Xóa khỏi danh sách so sánh' : 'Thêm vào so sánh'}
                              className={`w-full py-1 flex items-center justify-center gap-1 text-[10px] font-bold rounded-lg border transition ${
                                inCompare
                                  ? 'bg-emerald-500 text-white border-emerald-500 hover:bg-emerald-600'
                                  : wrongType || isFull
                                  ? 'bg-slate-50 text-slate-300 border-slate-200 cursor-not-allowed'
                                  : 'bg-white text-slate-500 border-slate-200 hover:bg-purple-50 hover:text-purple-600 hover:border-purple-300'
                              }`}
                            >
                              {inCompare
                                ? <><Check className="w-3 h-3" /> Đã chọn so sánh</>
                                : <><BarChart2 className="w-3 h-3" /> So sánh</>
                              }
                            </button>
                          );
                        })()}
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </section>

          </div>
        ) : activeTab === 'builder' ? (
          /* TRANG BUILDER CẤU HÌNH (Giao diện chuẩn An Phát PC / Nguyễn Công) */
          <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
            
            {/* Cột Trái: Bảng Ráp Cấu Hình (8 Cột) */}
            <div className="lg:col-span-8 space-y-4">
              <div className="flex items-center justify-between bg-white p-4 rounded-2xl border border-slate-200">
                <div>
                  <h2 className="text-base font-black text-slate-900 uppercase">Xây Dựng Cấu Hình PC Tự Chọn</h2>
                  <p className="text-xs text-slate-500">Đối chiếu trực tiếp Socket CPU, chuẩn RAM và công suất nguồn PSU</p>
                </div>
                <button 
                  onClick={() => setSelectedParts({})}
                  className="text-xs text-red-600 hover:underline font-bold"
                >
                  Làm mới dàn PC
                </button>
              </div>

              <div className="space-y-2.5">
                {categories.map((cat) => {
                  const part = selectedParts[cat.type];
                  return (
                    <div key={cat.id} className="bg-white border border-slate-200 rounded-2xl p-3.5 flex items-center justify-between gap-4 shadow-2xs hover:border-blue-300 transition">
                      <div className="flex items-center gap-3 min-w-0">
                        <div className="w-12 h-12 rounded-xl bg-slate-50 border border-slate-200 flex items-center justify-center shrink-0">
                          {renderIcon(cat.type)}
                        </div>
                        <div className="truncate">
                          <span className="text-[10px] font-black uppercase text-blue-600 tracking-wider block">{cat.name}</span>
                          {part ? (
                            <p className="text-xs font-bold text-slate-800 truncate">{part.name}</p>
                          ) : (
                            <p className="text-xs text-slate-400 italic">Vui lòng chọn linh kiện {cat.name}...</p>
                          )}
                        </div>
                      </div>

                      <div className="flex items-center gap-3 shrink-0">
                        {part ? (
                          <>
                            <span className="text-xs font-black text-red-600">{part.price?.toLocaleString('vi-VN')} đ</span>
                            <button
                              onClick={() => handleOpenReplacementSuggestions(part)}
                              className="px-2.5 py-1.5 rounded-lg bg-amber-50 hover:bg-amber-100 text-amber-700 text-[10px] font-bold transition border border-amber-200 flex items-center gap-1"
                            >
                              <Sparkles className="w-3.5 h-3.5" /> Gợi ý thay thế
                            </button>
                            <button onClick={() => handleRemovePart(cat.type)} className="p-1.5 hover:bg-red-50 text-red-500 rounded-lg transition" title="Xóa">
                              <Trash2 className="w-4 h-4" />
                            </button>
                          </>
                        ) : (
                          <button 
                            onClick={() => {
                              setModalCategory(cat);
                              setBuilderSearch('');
                              setBuilderFilters({});
                              setBuilderPriceRange('ALL');
                            }}
                            className="px-4 py-1.5 rounded-xl bg-blue-50 hover:bg-blue-100 text-blue-600 text-xs font-bold transition border border-blue-200"
                          >
                            + Chọn Linh Kiện
                          </button>
                        )}
                      </div>
                    </div>
                  );
                })}
              </div>
            </div>

            {/* Cột Phải: Bảng Tương Thích & Tính Tiền (4 Cột) */}
            <div className="lg:col-span-4 space-y-4">
              <div className="bg-white border border-slate-200 rounded-2xl p-5 sticky top-24 space-y-5 shadow-sm">
                <h3 className="text-xs font-black text-slate-900 uppercase tracking-wider border-b border-slate-100 pb-3 flex items-center gap-1.5">
                  <ShieldCheck className="w-4 h-4 text-blue-600" /> Bảng Kiểm Tra Tương Thích
                </h3>

                {/* Ước tính công suất */}
                <div className="space-y-1.5">
                  <div className="flex justify-between text-xs font-bold">
                    <span className="text-slate-600 flex items-center gap-1"><Zap className="w-3.5 h-3.5 text-amber-500" /> Công suất ước tính:</span>
                    <span className="text-slate-900 font-black">{estimatedTdp}W</span>
                  </div>
                  <div className="w-full bg-slate-100 h-2 rounded-full overflow-hidden">
                    <div 
                      className={`h-full transition-all ${estimatedTdp > (psuWattage || 500) ? 'bg-red-500' : 'bg-emerald-500'}`} 
                      style={{ width: `${Math.min((estimatedTdp / (psuWattage || 650)) * 100, 100)}%` }}
                    />
                  </div>
                  {psuWattage > 0 && <p className="text-[11px] text-slate-500">Nguồn đang chọn: <strong className="text-slate-800">{psuWattage}W</strong></p>}
                </div>

                {/* Danh sách cảnh báo tương thích */}
                <div className="space-y-2">
                  {compatibilityErrors.length > 0 ? (
                    compatibilityErrors.map((err, idx) => (
                      <div key={idx} className="flex gap-2 p-3 bg-red-50 border border-red-200 rounded-xl text-red-700 text-xs items-start">
                        <AlertCircle className="w-4 h-4 shrink-0 mt-0.5 text-red-600" />
                        <span>{err}</span>
                      </div>
                    ))
                  ) : (
                    <div className="flex gap-2 p-3 bg-emerald-50 border border-emerald-200 rounded-xl text-emerald-800 text-xs items-center">
                      <CheckCircle2 className="w-4 h-4 shrink-0 text-emerald-600" />
                      <span>Các linh kiện tương thích 100%!</span>
                    </div>
                  )}
                </div>

                {/* Box Thanh Toán */}
                <div className="border-t border-slate-100 pt-4 space-y-3">
                  <div className="flex justify-between items-baseline">
                    <span className="text-xs text-slate-500 font-bold uppercase">Tổng dự toán:</span>
                    <span className="text-xl font-black text-red-600">{totalPrice.toLocaleString('vi-VN')} đ</span>
                  </div>
                  
                  <button 
                    type="button"
                    onClick={handleAddBuildToCart}
                    disabled={compatibilityErrors.length > 0 || totalPrice === 0}
                    className="w-full py-3 bg-amber-400 hover:bg-amber-300 disabled:opacity-40 text-slate-900 font-black text-xs rounded-xl shadow-md uppercase tracking-wider transition"
                  >
                    Thêm cấu hình vào giỏ hàng
                  </button>
                  <p className="text-[11px] text-slate-400 text-center italic">Cấu hình chỉ vào giỏ sau khi bạn nhấn nút này.</p>
                </div>
              </div>
            </div>

          </div>
        ) : activeTab === 'cart' ? (
          <section className="max-w-6xl mx-auto space-y-5">
            <div className="flex flex-col sm:flex-row sm:items-end justify-between gap-3">
              <div>
                <h1 className="text-xl font-black text-slate-900">Giỏ hàng của bạn</h1>
                <p className="mt-1 text-xs text-slate-500">
                  {cartItems.reduce((count, item) => count + item.quantity, 0)} sản phẩm đã được thêm vào giỏ.
                  Linh kiện trong PC Builder không tự thêm vào đây.
                </p>
              </div>
              {cartItems.length > 0 && (
                <button
                  type="button"
                  onClick={() => setCartItems([])}
                  className="self-start sm:self-auto text-xs font-bold text-red-600 hover:text-red-700"
                >
                  Xóa toàn bộ giỏ hàng
                </button>
              )}
            </div>

            {orderSuccess && (
              <div className={`flex flex-col sm:flex-row sm:items-center justify-between gap-3 rounded-xl border p-4 ${
                orderSuccess.emailSent === false
                  ? 'border-amber-200 bg-amber-50 text-amber-900'
                  : 'border-emerald-200 bg-emerald-50 text-emerald-800'
              }`}>
                <div>
                  <p className="text-sm font-black">Đặt hàng thành công!</p>
                  <p className="mt-1 text-xs">Mã đơn: {orderSuccess.orderCode} · Tổng tiền {Number(orderSuccess.totalAmount).toLocaleString('vi-VN')} đ</p>
                  {orderSuccess.emailWarning && <p className="mt-1 text-xs">{orderSuccess.emailWarning}</p>}
                </div>
                <button type="button" onClick={() => setActiveTab('home')} className="text-xs font-bold underline">
                  Tiếp tục mua sắm
                </button>
              </div>
            )}

            {cartItems.length === 0 ? (
              <div className="rounded-2xl border border-slate-200 bg-white py-16 text-center shadow-sm">
                <ShoppingCart className="mx-auto h-12 w-12 text-slate-300" />
                <h2 className="mt-4 text-base font-black text-slate-800">
                  {orderSuccess ? 'Đơn hàng đã được tiếp nhận' : 'Giỏ hàng đang trống'}
                </h2>
                <p className="mt-1 text-xs text-slate-500">
                  {orderSuccess ? 'Giỏ hàng đã được làm trống sau khi đặt hàng thành công.' : 'Thêm sản phẩm từ cửa hàng hoặc thêm cấu hình ở mục Xây Dựng PC.'}
                </p>
                <button
                  type="button"
                  onClick={() => setActiveTab('home')}
                  className="mt-5 rounded-xl bg-blue-600 px-5 py-2.5 text-xs font-bold text-white hover:bg-blue-700"
                >
                  Tiếp tục mua sắm
                </button>
              </div>
            ) : (
              <div className="grid grid-cols-1 lg:grid-cols-3 gap-5 items-start">
                <div className="lg:col-span-2 space-y-3">
                  {cartItems.map(item => (
                    <article key={item.id} className="flex flex-col sm:flex-row sm:items-center gap-4 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
                      <div className="flex h-20 w-20 shrink-0 items-center justify-center overflow-hidden rounded-xl border border-slate-100 bg-slate-50">
                        {item.imageUrl?.startsWith('http')
                          ? <img src={item.imageUrl} alt={item.name} className="h-full w-full object-contain p-2" />
                          : renderIcon(item.categoryType)}
                      </div>
                      <div className="min-w-0 flex-1">
                        <p className="text-[10px] font-black uppercase text-blue-600">{item.categoryName || item.categoryType}</p>
                        <h2 className="mt-1 text-sm font-bold text-slate-800">{item.name}</h2>
                        <p className="mt-1 text-xs font-black text-red-600">{Number(item.price).toLocaleString('vi-VN')} đ</p>
                        <p className="mt-1 text-[10px] text-slate-500">Tồn kho: {item.stockQuantity}</p>
                      </div>
                      <div className="flex items-center justify-between sm:justify-end gap-4">
                        <div className="flex items-center gap-2 rounded-lg border border-slate-200 p-1">
                          <button
                            type="button"
                            aria-label={`Giảm số lượng ${item.name}`}
                            onClick={() => handleUpdateCartQuantity(item.id, item.quantity - 1)}
                            className="rounded p-1 text-slate-600 hover:bg-slate-100"
                          >
                            <Minus className="h-4 w-4" />
                          </button>
                          <span className="min-w-5 text-center text-xs font-bold">{item.quantity}</span>
                          <button
                            type="button"
                            aria-label={`Tăng số lượng ${item.name}`}
                            disabled={item.quantity >= (item.stockQuantity || 0)}
                            onClick={() => handleUpdateCartQuantity(item.id, item.quantity + 1)}
                            className="rounded p-1 text-slate-600 hover:bg-slate-100 disabled:cursor-not-allowed disabled:text-slate-300"
                          >
                            <Plus className="h-4 w-4" />
                          </button>
                        </div>
                        <p className="min-w-28 text-right text-xs font-black text-slate-900">
                          {(Number(item.price) * item.quantity).toLocaleString('vi-VN')} đ
                        </p>
                        <button
                          type="button"
                          aria-label={`Xóa ${item.name} khỏi giỏ hàng`}
                          onClick={() => handleUpdateCartQuantity(item.id, 0)}
                          className="rounded-lg p-2 text-red-500 hover:bg-red-50"
                        >
                          <Trash2 className="h-4 w-4" />
                        </button>
                      </div>
                    </article>
                  ))}
                </div>

                <aside className="space-y-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                  <h2 className="text-sm font-black uppercase text-slate-900">Tóm tắt đơn hàng</h2>
                  <div className="flex items-center justify-between text-xs text-slate-600">
                    <span>Giao hàng tiêu chuẩn</span>
                    <span className="font-bold text-emerald-700">Miễn phí</span>
                  </div>
                  <div className="flex items-center justify-between border-t border-slate-100 pt-4">
                    <span className="text-xs font-bold uppercase text-slate-600">Tổng tiền</span>
                    <span className="text-lg font-black text-red-600">
                      {cartItems.reduce((total, item) => total + Number(item.price) * item.quantity, 0).toLocaleString('vi-VN')} đ
                    </span>
                  </div>
                  <p className="text-[11px] text-slate-500">Chưa áp dụng mã giảm giá. Thanh toán khi nhận hàng (COD).</p>
                  <button
                    type="button"
                    onClick={() => {
                      setCheckoutError('');
                      setActiveTab('checkout');
                    }}
                    className="flex w-full items-center justify-center gap-2 rounded-xl bg-red-600 py-3 text-xs font-black uppercase text-white transition hover:bg-red-700"
                  >
                    <CreditCard className="h-4 w-4" />
                    Tiến hành thanh toán
                  </button>
                </aside>
              </div>
            )}
          </section>
        ) : activeTab === 'checkout' ? (
          <section className="max-w-6xl mx-auto space-y-5">
            <div>
              <button
                type="button"
                onClick={() => setActiveTab('cart')}
                className="text-xs font-bold text-blue-600 hover:text-blue-700"
              >
                ← Quay lại giỏ hàng
              </button>
              <h1 className="mt-3 text-xl font-black text-slate-900">Thông tin thanh toán</h1>
              <p className="mt-1 text-xs text-slate-500">Kiểm tra thông tin giao hàng và xác nhận đơn COD.</p>
            </div>

            {cartItems.length === 0 ? (
              <div className="rounded-2xl border border-slate-200 bg-white p-8 text-center">
                <p className="text-sm font-bold text-slate-700">Giỏ hàng đang trống.</p>
                <button type="button" onClick={() => setActiveTab('cart')} className="mt-3 text-xs font-bold text-blue-600 underline">
                  Quay lại giỏ hàng
                </button>
              </div>
            ) : (
              <form onSubmit={handleCheckout} className="grid grid-cols-1 items-start gap-5 lg:grid-cols-3">
                <div className="space-y-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm lg:col-span-2">
                  <h2 className="text-sm font-black uppercase text-slate-900">Thông tin người nhận</h2>
                  <label className="block text-xs font-semibold text-slate-600">
                    Họ và tên
                    <input
                      required
                      autoComplete="name"
                      value={checkoutForm.receiverName}
                      onChange={event => setCheckoutForm(form => ({ ...form, receiverName: event.target.value }))}
                      className="mt-1.5 w-full rounded-lg border border-slate-200 px-3 py-2.5 text-xs text-slate-800 focus:outline-none focus:border-blue-500"
                    />
                  </label>
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
                    <label className="block text-xs font-semibold text-slate-600">
                      Số điện thoại
                      <input
                        required
                        type="tel"
                        autoComplete="tel"
                        value={checkoutForm.receiverPhone}
                        onChange={event => setCheckoutForm(form => ({ ...form, receiverPhone: event.target.value }))}
                        className="mt-1.5 w-full rounded-lg border border-slate-200 px-3 py-2.5 text-xs text-slate-800 focus:outline-none focus:border-blue-500"
                      />
                    </label>
                    <label className="block text-xs font-semibold text-slate-600">
                      Email nhận xác nhận
                      <input
                        required
                        type="email"
                        autoComplete="email"
                        value={checkoutForm.email}
                        onChange={event => setCheckoutForm(form => ({ ...form, email: event.target.value }))}
                        className="mt-1.5 w-full rounded-lg border border-slate-200 px-3 py-2.5 text-xs text-slate-800 focus:outline-none focus:border-blue-500"
                      />
                    </label>
                  </div>
                  <label className="block text-xs font-semibold text-slate-600">
                    Địa chỉ giao hàng
                    <textarea
                      required
                      rows={3}
                      autoComplete="street-address"
                      value={checkoutForm.shippingAddress}
                      onChange={event => setCheckoutForm(form => ({ ...form, shippingAddress: event.target.value }))}
                      className="mt-1.5 w-full resize-y rounded-lg border border-slate-200 px-3 py-2.5 text-xs text-slate-800 focus:outline-none focus:border-blue-500"
                    />
                  </label>

                  <div className="space-y-3 border-t border-slate-100 pt-4">
                    <h2 className="text-sm font-black uppercase text-slate-900">Vận chuyển</h2>
                    <div className="flex items-start gap-3 rounded-xl border border-blue-200 bg-blue-50 p-4">
                      <input type="radio" checked readOnly aria-label="Giao hàng tiêu chuẩn" className="mt-0.5 accent-blue-600" />
                      <div className="flex-1">
                        <p className="text-xs font-bold text-slate-800">Giao hàng tiêu chuẩn</p>
                        <p className="mt-1 text-[11px] text-slate-500">Phí vận chuyển: miễn phí</p>
                      </div>
                      <span className="text-xs font-black text-emerald-700">0 đ</span>
                    </div>
                  </div>

                  <div className="space-y-3 border-t border-slate-100 pt-4">
                    <h2 className="text-sm font-black uppercase text-slate-900">Phương thức thanh toán</h2>
                    <label className="flex cursor-pointer items-start gap-3 rounded-xl border border-blue-200 bg-blue-50 p-4">
                      <input type="radio" checked readOnly aria-label="Thanh toán khi nhận hàng COD" className="mt-0.5 accent-blue-600" />
                      <div>
                        <p className="text-xs font-bold text-slate-800">Thanh toán khi nhận hàng (COD)</p>
                        <p className="mt-1 text-[11px] text-slate-500">Thanh toán trực tiếp cho nhân viên giao hàng.</p>
                      </div>
                    </label>
                  </div>
                  <p className="text-[11px] text-slate-500">Chưa áp dụng mã giảm giá.</p>
                </div>

                <aside className="space-y-4 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm">
                  <h2 className="text-sm font-black uppercase text-slate-900">Đơn hàng của bạn</h2>
                  <div className="max-h-72 space-y-3 overflow-y-auto">
                    {cartItems.map(item => (
                      <div key={item.id} className="flex justify-between gap-3 text-xs">
                        <span className="min-w-0 text-slate-600">{item.name} × {item.quantity}</span>
                        <span className="shrink-0 font-bold text-slate-800">
                          {(Number(item.price) * item.quantity).toLocaleString('vi-VN')} đ
                        </span>
                      </div>
                    ))}
                  </div>
                  <div className="space-y-2 border-t border-slate-100 pt-4 text-xs">
                    <div className="flex justify-between text-slate-600">
                      <span>Tạm tính</span>
                      <span>{cartItems.reduce((total, item) => total + Number(item.price) * item.quantity, 0).toLocaleString('vi-VN')} đ</span>
                    </div>
                    <div className="flex justify-between text-slate-600">
                      <span>Phí giao hàng</span>
                      <span className="font-bold text-emerald-700">Miễn phí</span>
                    </div>
                    <div className="flex justify-between border-t border-slate-100 pt-3 text-sm font-black text-slate-900">
                      <span>Tổng thanh toán</span>
                      <span className="text-red-600">
                        {cartItems.reduce((total, item) => total + Number(item.price) * item.quantity, 0).toLocaleString('vi-VN')} đ
                      </span>
                    </div>
                  </div>
                  {checkoutError && <p role="alert" className="rounded-lg bg-red-50 p-2.5 text-xs text-red-700">{checkoutError}</p>}
                  <button
                    type="submit"
                    disabled={checkoutLoading || cartItems.length === 0}
                    className="flex w-full items-center justify-center gap-2 rounded-xl bg-red-600 py-3 text-xs font-black uppercase text-white transition hover:bg-red-700 disabled:cursor-wait disabled:opacity-50"
                  >
                    {checkoutLoading && <Loader2 className="h-4 w-4 animate-spin" />}
                    {checkoutLoading ? 'Đang kiểm tra tồn kho và đặt hàng...' : 'Đặt hàng COD'}
                  </button>
                </aside>
              </form>
            )}
          </section>
        ) : activeTab === 'orders' ? (
          <section className="mx-auto max-w-6xl space-y-5">
            {orderUpdateNotice && (
              <div role="status" aria-live="polite" className="rounded-xl border border-blue-200 bg-blue-50 p-3 text-xs font-semibold text-blue-800">
                {orderUpdateNotice}
              </div>
            )}
            {selectedUserOrder ? (
              <>
                <button
                  type="button"
                  onClick={() => setSelectedUserOrder(null)}
                  className="text-xs font-bold text-blue-600 hover:text-blue-700"
                >
                  ← Quay lại danh sách đơn hàng
                </button>
                {orderDetailLoading ? (
                  <div className="flex justify-center rounded-2xl border border-slate-200 bg-white p-12">
                    <Loader2 className="h-6 w-6 animate-spin text-blue-600" />
                  </div>
                ) : orderDetailError ? (
                  <div role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-xs text-red-700">
                    {orderDetailError}
                  </div>
                ) : (
                  <>
                    <header className="rounded-2xl border border-slate-200 bg-white p-5">
                      <p className="text-[10px] font-bold uppercase text-slate-500">Chi tiết đơn hàng</p>
                      <h1 className="mt-1 text-lg font-black text-slate-900">{selectedUserOrder.orderCode}</h1>
                      <p className="mt-1 text-xs text-slate-500">
                        Đặt ngày {new Date(selectedUserOrder.createdAt).toLocaleString('vi-VN')}
                      </p>
                    </header>
                    <div className="grid grid-cols-1 gap-5 lg:grid-cols-3">
                      <div className="space-y-5 lg:col-span-2">
                        <section className="rounded-2xl border border-slate-200 bg-white p-5">
                          <h2 className="text-sm font-black uppercase text-slate-900">Tiến trình đơn hàng</h2>
                          {['Cancelled', 'DeliveryFailed', 'Refunded'].includes(selectedUserOrder.status) ? (
                            <p className="mt-4 rounded-xl bg-amber-50 p-4 text-xs font-bold text-amber-800">
                              {selectedUserOrder.status === 'Cancelled'
                                ? 'Đơn hàng đã bị hủy.'
                                : selectedUserOrder.status === 'DeliveryFailed'
                                  ? 'Giao hàng không thành công. Vui lòng liên hệ cửa hàng để được hỗ trợ.'
                                  : 'Đơn hàng đã được hoàn tiền.'}
                            </p>
                          ) : (
                            <ol className="mt-5 space-y-5">
                              {[
                                ['Pending', 'Chờ xác nhận'],
                                ['Confirmed', 'Đã xác nhận / Đang đóng gói'],
                                ['Shipping', 'Đã bàn giao vận chuyển / Đang giao hàng'],
                                ['Completed', 'Giao thành công'],
                              ].map(([status, label], index, steps) => {
                                const history = selectedUserOrder.statusHistory?.find(entry => entry.status === status);
                                const currentIndex = steps.findIndex(([stepStatus]) => stepStatus === selectedUserOrder.status);
                                const complete = (currentIndex >= 0 && index < currentIndex) || Boolean(history && status !== selectedUserOrder.status);
                                return (
                                  <li key={status} className="flex items-start gap-3">
                                    <span className={`mt-0.5 flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-xs font-black ${
                                      status === selectedUserOrder.status || complete
                                        ? 'bg-emerald-100 text-emerald-700'
                                        : 'bg-slate-100 text-slate-400'
                                    }`}>
                                      {complete ? '✓' : index + 1}
                                    </span>
                                    <div>
                                      <p className="text-xs font-bold text-slate-800">
                                        {label}{status === selectedUserOrder.status ? ' · Hiện tại' : ''}
                                      </p>
                                      {history && <p className="mt-1 text-[11px] text-slate-500">{new Date(history.changedAt).toLocaleString('vi-VN')}</p>}
                                      {history?.trackingNumber && (
                                        <p className="mt-1 text-[11px] font-semibold text-blue-700">{history.carrier && `${history.carrier} · `}Mã vận đơn: {history.trackingNumber}</p>
                                      )}
                                      {history?.changedByName && <p className="mt-1 text-[10px] text-slate-500">Cập nhật bởi: {history.changedByName}</p>}
                                      {history?.note && <p className="mt-1 text-[11px] text-slate-600">Ghi chú: {history.note}</p>}
                                    </div>
                                  </li>
                                );
                              })}
                            </ol>
                          )}
                        </section>
                        <section className="rounded-2xl border border-slate-200 bg-white p-5">
                          <h2 className="text-sm font-black uppercase text-slate-900">Sản phẩm đã mua</h2>
                          <div className="mt-3 divide-y divide-slate-100">
                            {selectedUserOrder.items?.map(item => {
                              const reviewKey = `${selectedUserOrder.id}:${item.productId}`;
                              const draft = reviewDrafts[reviewKey] || { rating: 5, comment: '', imageUrl: '', error: '' };
                              return (
                                <div key={item.productId} className="py-3 text-xs">
                                  <div className="flex justify-between gap-3">
                                    <span className="text-slate-700">{item.productName} × {item.quantity}</span>
                                    <span className="shrink-0 font-bold text-slate-900">
                                      {(Number(item.unitPrice) * item.quantity).toLocaleString('vi-VN')} đ
                                    </span>
                                  </div>
                                  {selectedUserOrder.status === 'Completed' && item.hasReviewed ? (
                                    <p className="mt-2 text-[11px] font-semibold text-emerald-700">✓ Bạn đã đánh giá sản phẩm này</p>
                                  ) : selectedUserOrder.status === 'Completed' ? (
                                    <div className="mt-3 rounded-xl border border-slate-200 bg-slate-50 p-3">
                                      <div className="flex gap-1" role="group" aria-label="Chọn số sao đánh giá">
                                        {[1, 2, 3, 4, 5].map(star => (
                                          <button key={star} type="button" onClick={() => setReviewDrafts(prev => ({ ...prev, [reviewKey]: { ...draft, rating: star } }))} aria-label={`${star} sao`}>
                                            <Star className={`h-5 w-5 ${star <= draft.rating ? 'fill-amber-400 text-amber-400' : 'text-slate-300'}`} />
                                          </button>
                                        ))}
                                      </div>
                                      <textarea
                                        value={draft.comment}
                                        onChange={event => setReviewDrafts(prev => ({ ...prev, [reviewKey]: { ...draft, comment: event.target.value, error: '' } }))}
                                        maxLength={1000}
                                        rows={3}
                                        placeholder="Chia sẻ nhận xét của bạn (không bắt buộc)"
                                        className="mt-2 w-full resize-y rounded-lg border border-slate-200 bg-white p-2"
                                      />
                                      <div className="mt-2 flex items-center gap-3">
                                        <label className="cursor-pointer rounded-lg border border-slate-200 bg-white px-3 py-2 text-[11px] font-semibold text-slate-700">
                                          Chọn ảnh
                                          <input type="file" accept="image/jpeg,image/png,image/webp" className="sr-only" onChange={event => handleReviewImageChange(selectedUserOrder.id, item.productId, event.target.files?.[0])} />
                                        </label>
                                        {draft.imageUrl && <span className="text-[11px] text-emerald-700">Đã chọn ảnh</span>}
                                        <button
                                          type="button"
                                          onClick={() => handleSubmitProductReview(selectedUserOrder.id, item.productId)}
                                          disabled={reviewSubmittingKey === reviewKey}
                                          className="ml-auto rounded-lg bg-blue-600 px-4 py-2 text-[11px] font-bold text-white disabled:opacity-50"
                                        >
                                          {reviewSubmittingKey === reviewKey ? 'Đang gửi...' : 'Gửi đánh giá'}
                                        </button>
                                      </div>
                                      {draft.error && <p role="alert" className="mt-2 text-[11px] text-red-600">{draft.error}</p>}
                                    </div>
                                  ) : null}
                                </div>
                              );
                            })}
                          </div>
                        </section>
                      </div>
                      <aside className="h-fit space-y-3 rounded-2xl border border-slate-200 bg-white p-5 text-xs">
                        <h2 className="text-sm font-black uppercase text-slate-900">Thông tin giao hàng</h2>
                        <p><span className="text-slate-500">Người nhận:</span> {selectedUserOrder.receiverName}</p>
                        <p><span className="text-slate-500">Điện thoại:</span> {selectedUserOrder.receiverPhone}</p>
                        <p><span className="text-slate-500">Địa chỉ:</span> {selectedUserOrder.shippingAddress}</p>
                        <p className="border-t border-slate-100 pt-3 font-black">
                          Tổng tiền <span className="float-right text-red-600">{Number(selectedUserOrder.totalAmount).toLocaleString('vi-VN')} đ</span>
                        </p>
                      </aside>
                    </div>
                  </>
                )}
              </>
            ) : (
              <>
                <div className="flex items-end justify-between gap-3">
                  <div>
                    <h1 className="text-xl font-black text-slate-900">Đơn hàng của tôi</h1>
                    <p className="mt-1 text-xs text-slate-500">Theo dõi trạng thái và lịch sử đơn hàng.</p>
                  </div>
                  <button
                    type="button"
                    onClick={handleOpenUserOrders}
                    disabled={ordersLoading}
                    className="rounded-lg border border-slate-200 bg-white px-3 py-2 text-xs font-bold text-slate-700 disabled:opacity-50"
                  >
                    Làm mới
                  </button>
                </div>
                {ordersLoading || orderDetailLoading ? (
                  <div className="flex justify-center rounded-2xl border border-slate-200 bg-white p-12">
                    <Loader2 className="h-6 w-6 animate-spin text-blue-600" />
                  </div>
                ) : ordersError || orderDetailError ? (
                  <div role="alert" className="rounded-xl border border-red-200 bg-red-50 p-4 text-xs text-red-700">
                    {ordersError || orderDetailError}
                  </div>
                ) : userOrders.length === 0 ? (
                  <div className="rounded-2xl border border-slate-200 bg-white py-14 text-center">
                    <ClipboardList className="mx-auto h-10 w-10 text-slate-300" />
                    <h2 className="mt-3 text-sm font-black text-slate-800">Bạn chưa có đơn hàng nào</h2>
                  </div>
                ) : (
                  <div className="space-y-3">
                    {userOrders.map(order => (
                      <article key={order.id} className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
                        <div className="flex flex-col justify-between gap-3 sm:flex-row sm:items-center">
                          <div>
                            <h2 className="text-sm font-black text-slate-900">{order.orderCode}</h2>
                            <p className="mt-1 text-[11px] text-slate-500">
                              Đặt ngày {new Date(order.createdAt).toLocaleString('vi-VN')} · {order.itemCount} sản phẩm
                            </p>
                            {order.trackingNumber && (
                              <p className="mt-1 text-[11px] font-semibold text-blue-700">{order.carrier && `${order.carrier} · `}Mã vận đơn: {order.trackingNumber}</p>
                            )}
                          </div>
                          <div className="flex items-center justify-between gap-4 sm:justify-end">
                            <span className="rounded-full bg-blue-50 px-3 py-1.5 text-[11px] font-bold text-blue-700">
                              {({
                                Pending: 'Chờ xử lý',
                                Confirmed: 'Đang đóng gói',
                                Shipping: 'Đã bàn giao vận chuyển / Đang giao hàng',
                                Completed: 'Giao thành công',
                                Cancelled: 'Đã hủy',
                                DeliveryFailed: 'Giao hàng thất bại',
                                Refunded: 'Đã hoàn tiền',
                              })[order.status] || order.status}
                            </span>
                            <button
                              type="button"
                              onClick={() => handleOpenUserOrder(order.id)}
                              className="rounded-lg bg-blue-600 px-3 py-2 text-[11px] font-bold text-white hover:bg-blue-700"
                            >
                              Xem chi tiết
                            </button>
                          </div>
                        </div>
                        <div className="mt-3 border-t border-slate-100 pt-3 text-sm font-black text-red-600">
                          {Number(order.totalAmount).toLocaleString('vi-VN')} đ
                        </div>
                      </article>
                    ))}
                  </div>
                )}
              </>
            )}
          </section>
        ) : activeTab === 'ai' ? (
          <section className="max-w-5xl mx-auto space-y-6">
            <div className="rounded-2xl bg-gradient-to-r from-slate-950 via-blue-950 to-blue-800 p-6 md:p-8 text-white shadow-lg">
              <div className="flex items-center gap-3">
                <div className="p-3 rounded-xl bg-amber-400 text-slate-900"><Sparkles className="w-6 h-6" /></div>
                <div>
                  <h1 className="text-xl md:text-2xl font-black uppercase">BUILD PC AI</h1>
                  <p className="mt-1 text-xs md:text-sm text-blue-100">Tạo cấu hình từ sản phẩm đang còn hàng, phù hợp nhu cầu và ngân sách.</p>
                </div>
              </div>
              <p className="mt-5 inline-flex rounded-lg border border-emerald-300/30 bg-emerald-400/10 px-3 py-2 text-[11px] font-semibold text-emerald-100">
                Cam kết ngân sách: tổng cấu hình không vượt quá 105% số tiền bạn nhập.
              </p>
            </div>

            <form onSubmit={handleGenerateAiBuild} className="grid grid-cols-1 lg:grid-cols-5 gap-5">
              <div className="lg:col-span-2 bg-white border border-slate-200 rounded-2xl p-5 space-y-5 shadow-sm">
                <h2 className="text-sm font-black text-slate-900">Nhu cầu của bạn</h2>
                <label className="block text-xs font-bold text-slate-600">
                  Ngân sách (triệu đồng)
                  <div className="relative mt-2">
                    <input
                      type="number"
                      min="1"
                      max="500"
                      step="0.5"
                      value={aiBudgetMillions}
                      onChange={event => setAiBudgetMillions(event.target.value)}
                      className="w-full rounded-xl border border-slate-200 px-4 py-3 pr-16 text-lg font-black text-slate-900 focus:outline-none focus:border-blue-500"
                      required
                    />
                    <span className="absolute right-4 top-3.5 text-xs font-bold text-slate-400">triệu</span>
                  </div>
                  <span className="mt-1 block text-[10px] font-normal text-slate-400">
                    Mức tối đa cho cấu hình này: {(Number(aiBudgetMillions || 0) * 1.05).toLocaleString('vi-VN', { maximumFractionDigits: 2 })} triệu đồng
                  </span>
                </label>

                <label className="block text-xs font-bold text-slate-600">
                  Mục đích sử dụng
                  <select
                    value={aiPurpose}
                    onChange={event => setAiPurpose(event.target.value)}
                    className="mt-2 w-full rounded-xl border border-slate-200 bg-white px-4 py-3 text-sm text-slate-800 focus:outline-none focus:border-blue-500"
                  >
                    <option value="gaming">Chơi game</option>
                    <option value="graphics">Đồ họa, dựng phim</option>
                    <option value="office">Văn phòng, học tập</option>
                    <option value="streaming">Gaming và livestream</option>
                    <option value="general">Đa dụng</option>
                  </select>
                </label>
                <label className="block text-xs font-bold text-slate-600">
                  Ghi chú yêu cầu
                  <textarea
                    value={aiNotes}
                    onChange={event => setAiNotes(event.target.value)}
                    maxLength={500}
                    rows={4}
                    placeholder="Ví dụ: CPU AMD, main ASUS"
                    className="mt-2 w-full resize-y rounded-xl border border-slate-200 px-4 py-3 text-sm font-normal text-slate-800 placeholder:text-slate-400 focus:outline-none focus:border-blue-500"
                  />
                  <span className="mt-1 block text-[10px] font-normal text-slate-400">
                    Ghi rõ loại linh kiện và thương hiệu mong muốn (ví dụ: CPU AMD, main ASUS). Nếu không có hàng đúng yêu cầu, AI sẽ báo thay vì tự đổi thương hiệu.
                    <span className="float-right">{aiNotes.length}/500</span>
                  </span>
                </label>
              </div>

              <div className="lg:col-span-3 bg-white border border-slate-200 rounded-2xl p-5 shadow-sm">
                <div className="flex items-start justify-between gap-3">
                  <div>
                    <h2 className="text-sm font-black text-slate-900">Linh kiện cần mua</h2>
                    <p className="mt-1 text-[11px] text-slate-500">AI chỉ chọn trong các loại bạn đánh dấu.</p>
                  </div>
                  <button
                    type="button"
                    onClick={() => setAiSelectedCategories(categories.filter(category => category.type !== 'Monitor' && category.type !== 'Other').map(category => category.type))}
                    className="text-[10px] font-bold text-blue-600 hover:text-blue-700"
                  >
                    Chọn linh kiện PC
                  </button>
                </div>
                <div className="mt-4 grid grid-cols-1 sm:grid-cols-2 gap-2">
                  {categories.filter(category => category.type !== 'Monitor' && category.type !== 'Other').map(category => {
                    const checked = aiSelectedCategories.includes(category.type);
                    return (
                      <label key={category.id} className={`flex items-center gap-3 rounded-xl border p-3 cursor-pointer transition ${
                        checked ? 'border-blue-300 bg-blue-50' : 'border-slate-200 hover:bg-slate-50'
                      }`}>
                        <input
                          type="checkbox"
                          checked={checked}
                          onChange={() => setAiSelectedCategories(current =>
                            checked
                              ? current.filter(type => type !== category.type)
                              : [...current, category.type]
                          )}
                          className="h-4 w-4 accent-blue-600"
                        />
                        <span className="text-slate-600">{renderIcon(category.type)}</span>
                        <span className="text-xs font-bold text-slate-800">{category.name}</span>
                      </label>
                    );
                  })}
                </div>
              </div>

              {aiError && (
                <div className="lg:col-span-5 rounded-xl border border-red-200 bg-red-50 p-3 text-xs font-semibold text-red-700">
                  {aiError}
                </div>
              )}

              <div className="lg:col-span-5 flex justify-center">
                <button
                  type="submit"
                  disabled={aiLoading}
                  className="inline-flex items-center justify-center gap-2 rounded-xl bg-amber-400 px-8 py-3 text-sm font-black text-slate-900 shadow-md transition hover:bg-amber-300 disabled:cursor-wait disabled:opacity-60"
                >
                  {aiLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />}
                  {aiLoading ? 'Đang tối ưu cấu hình...' : 'Tạo cấu hình với BUILD PC AI'}
                </button>
              </div>
            </form>

            {aiRecommendation && (
              <div className="overflow-hidden rounded-2xl border border-emerald-200 bg-white shadow-sm">
                <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3 bg-emerald-50 p-5">
                  <div>
                    <h2 className="text-sm font-black text-emerald-900">Cấu hình đề xuất</h2>
                    <p className="mt-1 text-[11px] text-emerald-800">{aiRecommendation.message}</p>
                  </div>
                  <div className="sm:text-right">
                    <p className="text-[10px] font-bold uppercase text-slate-500">Tổng tiền</p>
                    <p className="text-xl font-black text-red-600">{aiRecommendation.totalPrice.toLocaleString('vi-VN')} đ</p>
                    <p className="text-[10px] text-slate-500">
                      Tối đa {aiRecommendation.maxAllowedPrice.toLocaleString('vi-VN')} đ
                    </p>
                  </div>
                </div>
                <div className="divide-y divide-slate-100">
                  {aiRecommendation.items.map(item => (
                    <div key={item.id} className="flex items-center justify-between gap-4 p-4">
                      <div className="flex items-center gap-3 min-w-0">
                        <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-slate-50">{renderIcon(item.categoryType)}</div>
                        <div className="min-w-0">
                          <p className="text-[10px] font-black uppercase text-blue-600">{item.categoryName}</p>
                          <p className="truncate text-xs font-bold text-slate-800">{item.name}</p>
                          <p className="text-[10px] text-slate-500">{item.brand} · Còn hàng: {item.stockQuantity}</p>
                        </div>
                      </div>
                      <p className="shrink-0 text-xs font-black text-slate-900">{item.price.toLocaleString('vi-VN')} đ</p>
                    </div>
                  ))}
                </div>
                <div className="flex flex-col sm:flex-row items-center justify-between gap-3 border-t border-slate-100 p-4">
                  <p className="text-[10px] text-slate-500">Tổng mức sử dụng ngân sách: {aiRecommendation.budgetUsagePercent.toFixed(1)}%</p>
                  <button
                    type="button"
                    onClick={handleUseAiBuild}
                    className="w-full sm:w-auto rounded-xl bg-blue-600 px-5 py-2.5 text-xs font-bold text-white transition hover:bg-blue-700"
                  >
                    Dùng cấu hình này trong Xây Dựng PC
                  </button>
                </div>
              </div>
            )}
          </section>
        ) : null}
      </main>

      {/* 4. MODAL XEM CHI TIẾT SẢN PHẨM (Chuẩn Form Nguyễn Công PC) */}
      {isDetailOpen && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-xs flex items-center justify-center p-3 overflow-y-auto animate-in fade-in duration-150">
          <div className="bg-white text-slate-800 rounded-2xl max-w-4xl w-full max-h-[90vh] flex flex-col shadow-2xl overflow-hidden my-auto border border-slate-200">
            
            <div className="p-4 border-b border-slate-200 flex justify-between items-center bg-slate-50">
              <div className="text-xs text-slate-500 flex items-center gap-1.5 font-medium truncate">
                <span>Linh kiện máy tính</span>
                <ChevronRight className="w-3.5 h-3.5 text-slate-400" />
                <span className="text-blue-600 font-bold">{selectedProduct?.categoryName}</span>
              </div>
              <button onClick={() => setIsDetailOpen(false)} className="text-slate-400 hover:text-slate-700">
                <X className="w-5 h-5" />
              </button>
            </div>

            {detailLoading ? (
              <div className="py-20 text-center space-y-2">
                <Loader2 className="w-8 h-8 text-blue-600 animate-spin mx-auto" />
                <p className="text-xs font-bold text-slate-500">Đang tải thông số kỹ thuật...</p>
              </div>
            ) : selectedProduct && (
              <div className="overflow-y-auto p-5 space-y-6 flex-1">
                <div className="grid grid-cols-1 md:grid-cols-12 gap-6">
                  
                  {/* Cột trái: Ảnh và chính sách */}
                  <div className="md:col-span-6 space-y-4">
                    <div className="w-full h-64 bg-slate-50 border border-slate-200 rounded-2xl flex items-center justify-center p-4 relative">
                      {selectedProduct.imageUrl && selectedProduct.imageUrl.startsWith('http') ? (
                        <img src={selectedProduct.imageUrl} alt={selectedProduct.name} className="max-h-full object-contain" />
                      ) : (
                        <div className="p-6 bg-white rounded-2xl border border-slate-200">
                          {renderIcon(selectedProduct.categoryType)}
                        </div>
                      )}
                    </div>

                    <div className="p-3 bg-slate-50 border border-slate-200 rounded-xl space-y-2 text-xs">
                      <span className="font-bold text-slate-800 uppercase block text-[11px]">Chính sách bán hàng</span>
                      <div className="grid grid-cols-2 gap-2 text-[11px] text-slate-600">
                        <div className="flex items-center gap-1"><ShieldCheck className="w-3.5 h-3.5 text-blue-600" /> Cam kết chính hãng</div>
                        <div className="flex items-center gap-1"><Truck className="w-3.5 h-3.5 text-blue-600" /> Giao hàng toàn quốc</div>
                        <div className="flex items-center gap-1"><RotateCcw className="w-3.5 h-3.5 text-blue-600" /> Đổi mới 30 ngày</div>
                        <div className="flex items-center gap-1"><CreditCard className="w-3.5 h-3.5 text-blue-600" /> Trả góp 0%</div>
                      </div>
                    </div>
                  </div>

                  {/* Cột phải: Giá và nút mua */}
                  <div className="md:col-span-6 space-y-4">
                    <div>
                      <span className="text-[10px] font-bold text-blue-600 uppercase">{selectedProduct.brand}</span>
                      <h2 className="text-base font-black text-slate-900 leading-snug">{selectedProduct.name}</h2>
                      <p className="text-xs text-slate-500 mt-1">Mã sản phẩm: <strong className="text-slate-700">{selectedProduct.sku || 'PC' + selectedProduct.id}</strong></p>
                    </div>

                    <div className="bg-red-50 border border-red-200 p-3 rounded-xl">
                      <span className="text-xs text-red-700 font-bold block">Giá bán khuyến mãi:</span>
                      <div className="text-2xl font-black text-red-600">
                        {selectedProduct.price?.toLocaleString('vi-VN')} đ
                      </div>
                      {selectedProduct.additionalSpecs?.regularPrice > selectedProduct.price && (
                        <div className="text-xs text-slate-500 line-through">
                          {Number(selectedProduct.additionalSpecs.regularPrice).toLocaleString('vi-VN')} đ
                        </div>
                      )}
                      <span className="text-[11px] text-slate-500 italic">Giá đã bao gồm 10% VAT & Bảo hành 36 tháng</span>
                    </div>

                    <div className="space-y-2">
                      <button 
                        onClick={() => {
                          handleSelectPart(selectedProduct);
                          setIsDetailOpen(false);
                          setActiveTab('builder');
                        }}
                        className="w-full py-3 bg-red-600 hover:bg-red-700 text-white font-black text-xs rounded-xl shadow-md uppercase transition"
                      >
                        Chọn Linh Kiện Này Vào Cấu Hình PC
                      </button>

                      <button
                        type="button"
                        onClick={() => handleAddProductToCart(selectedProduct)}
                        disabled={(selectedProduct.stockQuantity || 0) < 1}
                        className="w-full py-2.5 bg-amber-400 hover:bg-amber-300 disabled:bg-slate-200 disabled:text-slate-400 text-slate-900 font-bold text-xs rounded-xl flex items-center justify-center gap-2 transition"
                      >
                        <ShoppingCart className="w-4 h-4" />
                        {(selectedProduct.stockQuantity || 0) > 0 ? 'Thêm sản phẩm vào giỏ hàng' : 'Sản phẩm hết hàng'}
                      </button>

                      <button 
                        onClick={() => {
                          setIsDetailOpen(false);
                          setActiveTab('ai');
                        }}
                        className="w-full py-2.5 bg-blue-50 hover:bg-blue-100 text-blue-600 font-bold text-xs rounded-xl border border-blue-200 flex items-center justify-center gap-1.5 transition"
                      >
                        <Sparkles className="w-4 h-4 text-blue-600" /> Gợi ý cấu hình với BUILD PC AI
                      </button>
                    </div>
                  </div>

                </div>

                {/* Bảng thông số chi tiết */}
                <div className="border border-slate-200 rounded-xl overflow-hidden">
                  <div className="bg-slate-100 px-4 py-2.5 font-bold text-xs text-slate-800 uppercase">
                    Thông số kỹ thuật chi tiết
                  </div>
                  <div className="p-4 divide-y divide-slate-100 text-xs">
                    <div className="grid grid-cols-3 py-2">
                      <span className="text-slate-500">Thương hiệu:</span>
                      <strong className="col-span-2 text-slate-800">{selectedProduct.brand}</strong>
                    </div>
                    {selectedProduct.specs?.socket && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">{selectedProduct.categoryType === 'Cooler' ? 'Socket hỗ trợ:' : 'Socket:'}</span>
                        <strong className="col-span-2 text-blue-600">{selectedProduct.specs.socket}</strong>
                      </div>
                    )}
                    {selectedProduct.specs?.chipset && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Chipset:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.specs.chipset}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.coreCount && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Số nhân / luồng:</span>
                        <strong className="col-span-2 text-slate-800">
                          {selectedProduct.additionalSpecs.coreCount} nhân / {selectedProduct.additionalSpecs.threadCount} luồng
                        </strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.baseClock && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Xung cơ bản / tối đa:</span>
                        <strong className="col-span-2 text-slate-800">
                          {selectedProduct.additionalSpecs.baseClock} / {selectedProduct.additionalSpecs.boostClock}
                        </strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.cache && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Bộ nhớ đệm:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.cache}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.vramGb && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">VRAM:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.vramGb} GB</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.capacityGb && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Dung lượng:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.capacityGb} GB</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.moduleCount && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Số thanh RAM:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.moduleCount}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.voltageV && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Điện áp:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.voltageV} V</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.interfaceType && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Giao tiếp:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.interfaceType}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.storageType && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Loại ổ:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.storageType}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.efficiencyRating && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Hiệu suất nguồn:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.efficiencyRating}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.modularType && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Kiểu dây nguồn:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.modularType}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.maxGpuLengthMm && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">GPU tối đa:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.maxGpuLengthMm} mm</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.maxCpuCoolerHeightMm && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Tản khí tối đa:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.maxCpuCoolerHeightMm} mm</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.radiatorSupport && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Hỗ trợ radiator:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.radiatorSupport}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.driveBays !== undefined && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Khay ổ đĩa:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.driveBays}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.coolingType && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Loại tản nhiệt:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.coolingType}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.radiatorSizeMm > 0 && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Kích thước radiator:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.radiatorSizeMm} mm</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.fanSizeMm && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Kích thước quạt:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.fanSizeMm} mm</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.heightMm && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Chiều cao tản nhiệt:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.heightMm} mm</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.readSpeed && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Tốc độ đọc / ghi:</span>
                        <strong className="col-span-2 text-slate-800">
                          {selectedProduct.additionalSpecs.readSpeed} / {selectedProduct.additionalSpecs.writeSpeed} MB/s
                        </strong>
                      </div>
                    )}
                    {selectedProduct.specs?.ramSlots > 0 && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Khe RAM:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.specs.ramSlots}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.maxMemoryGb && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Dung lượng RAM tối đa:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.maxMemoryGb} GB</strong>
                      </div>
                    )}
                    {selectedProduct.specs?.formFactor && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Form factor:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.specs.formFactor}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.m2Slots !== undefined && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Khe M.2:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.m2Slots}</strong>
                      </div>
                    )}
                    {selectedProduct.specs?.ramType && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Loại bộ nhớ:</span>
                        <strong className="col-span-2 text-blue-600">{selectedProduct.specs.ramType}</strong>
                      </div>
                    )}
                    {selectedProduct.specs?.ramBusSpeed > 0 && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Tốc độ RAM:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.specs.ramBusSpeed} MHz</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.formFactor && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Form factor:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.additionalSpecs.formFactor}</strong>
                      </div>
                    )}
                    {selectedProduct.additionalSpecs?.description && (
                      <div className="grid grid-cols-3 gap-3 py-2">
                        <span className="text-slate-500">Mô tả:</span>
                        <strong className="col-span-2 text-slate-800 font-medium">{selectedProduct.additionalSpecs.description}</strong>
                      </div>
                    )}
                    {selectedProduct.specs?.tdpWattage > 0 && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">
                          {selectedProduct.categoryType === 'PSU' ? 'Công suất nguồn:' : selectedProduct.categoryType === 'Cooler' ? 'TDP hỗ trợ:' : 'Công suất tiêu thụ (TDP):'}
                        </span>
                        <strong className="col-span-2 text-amber-600">{selectedProduct.specs.tdpWattage}W</strong>
                      </div>
                    )}
                    {selectedProduct.specs?.recommendedPsu > 0 && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Nguồn khuyến nghị:</span>
                        <strong className="col-span-2 text-red-600">≥ {selectedProduct.specs.recommendedPsu}W</strong>
                      </div>
                    )}
                    {selectedProduct.specs?.lengthMm > 0 && (
                      <div className="grid grid-cols-3 py-2">
                        <span className="text-slate-500">Chiều dài card:</span>
                        <strong className="col-span-2 text-slate-800">{selectedProduct.specs.lengthMm} mm</strong>
                      </div>
                    )}
                  </div>
                </div>

                <section className="border border-slate-200 rounded-xl overflow-hidden">
                  <div className="bg-slate-100 px-4 py-3 flex items-center justify-between gap-3">
                    <h3 className="font-bold text-xs text-slate-800 uppercase">Đánh giá sản phẩm</h3>
                    <div className="flex items-center gap-1.5 text-amber-500" aria-label={`Đánh giá trung bình ${productReviews.length ? (productReviews.reduce((sum, review) => sum + review.rating, 0) / productReviews.length).toFixed(1) : '0'} trên 5`}>
                      <Star className="w-4 h-4 fill-current" />
                      <strong className="text-slate-800">{productReviews.length
                        ? (productReviews.reduce((sum, review) => sum + review.rating, 0) / productReviews.length).toFixed(1)
                        : '0.0'}</strong>
                      <span className="text-[11px] text-slate-500">({productReviews.length} đánh giá)</span>
                    </div>
                  </div>
                  {productReviews.length === 0 ? (
                    <p className="p-4 text-xs text-slate-500">Sản phẩm chưa có đánh giá.</p>
                  ) : (
                    <div className="divide-y divide-slate-100">
                      {productReviews.map(review => (
                        <article key={review.id} className="p-4">
                          <div className="flex items-center justify-between gap-3">
                            <strong className="text-xs text-slate-800">{review.userName || 'Khách hàng'}</strong>
                            <time className="text-[10px] text-slate-500">{new Date(review.createdAt).toLocaleDateString('vi-VN')}</time>
                          </div>
                          <div className="mt-1 flex gap-0.5 text-amber-500" aria-label={`${review.rating} trên 5 sao`}>
                            {[1, 2, 3, 4, 5].map(star => <Star key={star} className={`w-3.5 h-3.5 ${star <= review.rating ? 'fill-current' : 'text-slate-200'}`} />)}
                          </div>
                          {review.comment && <p className="mt-2 whitespace-pre-wrap text-xs text-slate-700">{review.comment}</p>}
                          {review.imageUrl && <img src={review.imageUrl} alt="Ảnh khách hàng gửi kèm đánh giá" className="mt-3 max-h-48 rounded-lg border border-slate-200 object-contain" />}
                        </article>
                      ))}
                    </div>
                  )}
                </section>
              </div>
            )}
          </div>
        </div>
      )}

      {/* 5. MODAL CHỌN LINH KIỆN CHO DÀN PC */}
      {modalCategory && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="bg-white border border-slate-200 rounded-2xl max-w-2xl w-full max-h-[80vh] flex flex-col shadow-2xl">
            <div className="p-4 border-b border-slate-200 bg-slate-50 rounded-t-2xl space-y-3">
              <div className="flex justify-between items-center">
                <h3 className="text-xs font-black text-slate-800 uppercase tracking-wider">Chọn {modalCategory.name}</h3>
                <button onClick={() => { setModalCategory(null); setBuilderSearch(''); setBuilderFilters({}); setBuilderPriceRange('ALL'); }} className="text-slate-400 hover:text-slate-600">
                  <X className="w-5 h-5" />
                </button>
              </div>
              {/* Thanh tìm kiếm & Ràng buộc */}
              <div className="flex flex-col gap-2">
                <div className="relative">
                  <Search className="absolute left-3 top-2.5 w-4 h-4 text-slate-400" />
                  <input
                    type="text"
                    placeholder={`Tìm kiếm ${modalCategory.name}...`}
                    value={builderSearch}
                    onChange={e => setBuilderSearch(e.target.value)}
                    className="w-full pl-9 pr-4 py-2 bg-white border border-slate-200 rounded-xl text-xs focus:outline-none focus:border-blue-500"
                  />
                </div>
                {(() => {
                  const compatibleProducts = getCompatibleProducts(modalCategory.type);
                  const filterFields = getBuilderFilterFields(modalCategory.type);
                  const hasActiveFilters = builderPriceRange !== 'ALL' || Object.values(builderFilters).some(value => value && value !== 'ALL');

                  return (
                    <>
                      <div className="grid grid-cols-2 sm:grid-cols-3 gap-2">
                        <label className="text-[10px] font-semibold text-slate-500">
                          Khoảng giá
                          <select
                            value={builderPriceRange}
                            onChange={e => setBuilderPriceRange(e.target.value)}
                            className="mt-1 w-full rounded-lg border border-slate-200 bg-white px-2 py-2 text-xs text-slate-700 focus:outline-none focus:border-blue-500"
                          >
                            <option value="ALL">Tất cả mức giá</option>
                            <option value="UNDER_2M">Dưới 2 triệu</option>
                            <option value="2M_5M">2 - 5 triệu</option>
                            <option value="5M_10M">5 - 10 triệu</option>
                            <option value="10M_20M">10 - 20 triệu</option>
                            <option value="OVER_20M">Trên 20 triệu</option>
                          </select>
                        </label>
                        {filterFields.map(field => {
                          const values = [...new Set(compatibleProducts.flatMap(product => {
                            const value = field.getValue(product);
                            if (value == null || value === '') return [];
                            const fieldValues = field.key === 'ramType' ? String(value).split(',') : [value];
                            return fieldValues.map(item => String(item).trim()).filter(Boolean);
                          }))].sort((a, b) => a.localeCompare(b, 'vi', { numeric: true }));

                          if (values.length === 0) return null;

                          return (
                            <label key={field.key} className="text-[10px] font-semibold text-slate-500">
                              {field.label}
                              <select
                                value={builderFilters[field.key] || 'ALL'}
                                onChange={e => setBuilderFilters(prev => ({ ...prev, [field.key]: e.target.value }))}
                                className="mt-1 w-full rounded-lg border border-slate-200 bg-white px-2 py-2 text-xs text-slate-700 focus:outline-none focus:border-blue-500"
                              >
                                <option value="ALL">Tất cả</option>
                                {values.map(value => (
                                  <option key={value} value={value}>{value}{field.unit || ''}</option>
                                ))}
                              </select>
                            </label>
                          );
                        })}
                      </div>
                      {hasActiveFilters && (
                        <button
                          type="button"
                          onClick={() => { setBuilderFilters({}); setBuilderPriceRange('ALL'); }}
                          className="text-left text-[10px] font-bold text-blue-600 hover:text-blue-700"
                        >
                          Xóa bộ lọc
                        </button>
                      )}
                    </>
                  );
                })()}
                {/* Hiển thị chú thích ràng buộc */}
                <div className="flex flex-wrap gap-2 text-[10px]">
                  {modalCategory.type === 'CPU' && mb && <span className="px-2 py-1 bg-amber-50 text-amber-700 border border-amber-200 rounded-md">Đã khoá Socket: {mb.socket} (Theo Mainboard)</span>}
                  {modalCategory.type === 'Mainboard' && cpu && <span className="px-2 py-1 bg-amber-50 text-amber-700 border border-amber-200 rounded-md">Đã khoá Socket: {cpu.socket} (Theo CPU)</span>}
                  {modalCategory.type === 'Mainboard' && ram && <span className="px-2 py-1 bg-amber-50 text-amber-700 border border-amber-200 rounded-md">Đã khoá RAM: {ram.ramType} (Theo RAM)</span>}
                  {modalCategory.type === 'RAM' && mb && <span className="px-2 py-1 bg-amber-50 text-amber-700 border border-amber-200 rounded-md">Đã khoá RAM: {mb.ramType} (Theo Mainboard)</span>}
                </div>
              </div>
            </div>

            <div className="p-4 overflow-y-auto space-y-2.5 flex-1">
              {(() => {
                const compatibleList = getFilteredBuilderProducts(
                  modalCategory.type,
                  getCompatibleProducts(modalCategory.type)
                );
                if (compatibleList.length === 0) {
                  return (
                    <div className="text-center py-8 text-slate-400 text-xs">
                      Không tìm thấy sản phẩm nào phù hợp với yêu cầu hoặc ràng buộc hệ thống.
                    </div>
                  );
                }
                return compatibleList.map(product => (
                  <div key={product.id} className="p-3 bg-white hover:bg-blue-50/50 border border-slate-200 rounded-xl flex items-center justify-between gap-4 transition">
                    <div className="flex items-center gap-3">
                      <div className="w-10 h-10 bg-slate-50 rounded-lg border border-slate-200 flex items-center justify-center">
                        {renderIcon(product.categoryType)}
                      </div>
                      <div>
                        <p className="text-xs font-bold text-slate-800">{product.name}</p>
                        <p className="text-[10px] text-slate-500">{product.brand} {product.socket ? `| Socket: ${product.socket}` : ''} {product.ramType ? `| RAM: ${product.ramType}` : ''}</p>
                      </div>
                    </div>
                    <div className="text-right shrink-0">
                      <p className="text-red-600 font-bold text-xs mb-1">{product.price?.toLocaleString('vi-VN')} đ</p>
                      <button onClick={() => handleSelectPart(product)} className="px-3.5 py-1 bg-blue-600 text-white text-xs font-bold rounded-lg hover:bg-blue-700 shadow-xs">
                        Chọn
                      </button>
                    </div>
                  </div>
                ));
              })()}
            </div>
          </div>
        </div>
      )}

      {replacementPart && (
        <div className="fixed inset-0 z-[60] bg-black/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="bg-white border border-slate-200 rounded-2xl max-w-3xl w-full max-h-[90vh] flex flex-col shadow-2xl">
            <div className="p-5 border-b border-slate-200 bg-slate-50 rounded-t-2xl">
              <div className="flex items-start justify-between gap-4">
                <div>
                  <h3 className="text-sm font-black text-slate-900">Gợi ý nâng cấp và thay thế linh kiện</h3>
                  <p className="mt-1 text-xs text-slate-500">
                    {replacementPart.categoryName || replacementPart.categoryType}: {replacementPart.name}
                  </p>
                </div>
                <button
                  onClick={() => { setReplacementPart(null); setSelectedReplacementId(null); }}
                  className="text-slate-400 hover:text-slate-600"
                  aria-label="Đóng gợi ý thay thế"
                >
                  <X className="w-5 h-5" />
                </button>
              </div>
              <p className="mt-3 text-[11px] text-slate-600">
                Chỉ hiển thị linh kiện cùng danh mục, còn hàng, giá trong khoảng{' '}
                <strong>{(replacementPart.price * 0.8).toLocaleString('vi-VN')} đ - {(replacementPart.price * 1.2).toLocaleString('vi-VN')} đ</strong>
                {' '}và tương thích với cấu hình hiện tại.
              </p>
            </div>

            <div className="p-4 overflow-y-auto space-y-3 flex-1">
              {replacementLoading ? (
                <div className="py-12 text-center text-slate-500">
                  <Loader2 className="w-6 h-6 mx-auto mb-2 animate-spin text-blue-600" />
                  <p className="text-xs">Đang kiểm tra kho hàng và độ tương thích...</p>
                </div>
              ) : replacementError ? (
                <div className="p-3 rounded-xl border border-red-200 bg-red-50 text-xs text-red-700">{replacementError}</div>
              ) : getReplacementSuggestions().length === 0 ? (
                <div className="py-10 text-center text-xs text-slate-500">
                  Không tìm thấy linh kiện còn hàng, phù hợp giá và tương thích với cấu hình hiện tại.
                </div>
              ) : (
                getReplacementSuggestions().map(product => {
                  const isSelected = selectedReplacementId === product.id;
                  const priceDifference = product.price - replacementPart.price;
                  return (
                    <button
                      key={product.id}
                      type="button"
                      onClick={() => setSelectedReplacementId(product.id)}
                      className={`w-full p-3 text-left border rounded-xl transition ${
                        isSelected ? 'border-blue-500 bg-blue-50 ring-1 ring-blue-200' : 'border-slate-200 hover:border-blue-300 hover:bg-slate-50'
                      }`}
                    >
                      <div className="flex flex-col sm:flex-row sm:items-center justify-between gap-3">
                        <div className="flex items-start gap-3 min-w-0">
                          <div className={`mt-0.5 w-4 h-4 shrink-0 rounded-full border flex items-center justify-center ${isSelected ? 'border-blue-600' : 'border-slate-300'}`}>
                            {isSelected && <span className="w-2 h-2 rounded-full bg-blue-600" />}
                          </div>
                          <div className="min-w-0">
                            <p className="text-xs font-bold text-slate-800">{product.name}</p>
                            <p className="mt-1 text-[10px] text-slate-500">
                              {product.brand || 'Không rõ thương hiệu'} · Còn {product.stockQuantity} sản phẩm
                            </p>
                            <p className="mt-1 text-[10px] text-slate-400">{product.performanceBasis}</p>
                          </div>
                        </div>
                        <div className="sm:text-right shrink-0 pl-7 sm:pl-0">
                          <p className="text-sm font-black text-red-600">{product.price.toLocaleString('vi-VN')} đ</p>
                          <p className={`text-[10px] font-bold ${priceDifference > 0 ? 'text-amber-700' : priceDifference < 0 ? 'text-emerald-700' : 'text-slate-500'}`}>
                            Chênh {priceDifference > 0 ? '+' : ''}{priceDifference.toLocaleString('vi-VN')} đ
                          </p>
                          <p className={`text-[10px] font-bold ${product.performanceChange > 0 ? 'text-emerald-700' : product.performanceChange < 0 ? 'text-amber-700' : 'text-slate-500'}`}>
                            Hiệu năng ước tính {product.performanceChange > 0 ? '+' : ''}{product.performanceChange.toFixed(1)}%
                          </p>
                        </div>
                      </div>
                    </button>
                  );
                })
              )}
            </div>

            <div className="p-4 border-t border-slate-200 bg-slate-50 rounded-b-2xl flex flex-col sm:flex-row items-center justify-between gap-3">
              <p className="text-[10px] text-slate-500 text-center sm:text-left">
                Sau khi xác nhận, giá và công suất cấu hình sẽ được tính lại tự động.
              </p>
              <button
                type="button"
                onClick={handleConfirmReplacement}
                disabled={!selectedReplacementId || replacementLoading}
                className="w-full sm:w-auto px-5 py-2.5 bg-blue-600 hover:bg-blue-700 disabled:bg-slate-300 text-white text-xs font-bold rounded-xl transition"
              >
                Xác nhận đổi linh kiện
              </button>
            </div>
          </div>
        </div>
      )}

      {/* 6. MODAL HỒ SƠ CÁ NHÂN */}
      {isProfileOpen && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="bg-white border border-slate-200 rounded-xl max-w-xl w-full max-h-[90vh] overflow-y-auto p-6 shadow-2xl relative text-slate-800">
            <button onClick={() => setIsProfileOpen(false)} className="absolute top-5 right-5 text-slate-400 hover:text-slate-600">
              <X className="w-5 h-5" />
            </button>

            <div className="flex items-center gap-3 mb-5">
              <div className="p-2.5 rounded-xl bg-blue-50 text-blue-600">
                <Settings className="w-5 h-5" />
              </div>
              <div>
                <h2 className="text-base font-black text-slate-900">Hồ Sơ Khách Hàng</h2>
                <p className="text-xs text-slate-500">{currentUser?.email}</p>
              </div>
            </div>

            {profileError && <div className="mb-3 p-3 bg-red-50 border border-red-200 text-red-700 text-xs rounded-xl">{profileError}</div>}
            {profileSuccess && <div className="mb-3 p-3 bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs rounded-xl">{profileSuccess}</div>}

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-3 text-xs mb-5">
              <div>
                <label className="block text-slate-500 mb-1 font-semibold">Mã người dùng</label>
                <input value={currentUser?.id || ''} readOnly className="w-full bg-slate-100 border border-slate-200 rounded-md px-3 py-2 text-slate-600" />
              </div>
              <div>
                <label className="block text-slate-500 mb-1 font-semibold">Email đăng nhập</label>
                <input value={profileForm.email} readOnly className="w-full bg-slate-100 border border-slate-200 rounded-md px-3 py-2 text-slate-600" />
              </div>
              <div>
                <label className="block text-slate-500 mb-1 font-semibold">Vai trò</label>
                <input value={profileForm.role} readOnly className="w-full bg-slate-100 border border-slate-200 rounded-md px-3 py-2 text-slate-600" />
              </div>
              <div>
                <label className="block text-slate-500 mb-1 font-semibold">Trạng thái tài khoản</label>
                <input value={profileForm.status} readOnly className="w-full bg-slate-100 border border-slate-200 rounded-md px-3 py-2 text-slate-600" />
              </div>
              <div>
                <label className="block text-slate-500 mb-1 font-semibold">Ngày tạo</label>
                <input value={profileForm.createdAt ? new Date(profileForm.createdAt).toLocaleString('vi-VN') : ''} readOnly className="w-full bg-slate-100 border border-slate-200 rounded-md px-3 py-2 text-slate-600" />
              </div>
              <div>
                <label className="block text-slate-500 mb-1 font-semibold">Cập nhật lần cuối</label>
                <input value={profileForm.updatedAt ? new Date(profileForm.updatedAt).toLocaleString('vi-VN') : ''} readOnly className="w-full bg-slate-100 border border-slate-200 rounded-md px-3 py-2 text-slate-600" />
              </div>
            </div>

            <form onSubmit={handleProfileSubmit} className="space-y-3 text-xs">
              <h3 className="font-black text-slate-800 border-b border-slate-100 pb-2">Thông tin có thể chỉnh sửa</h3>
              <div>
                <label className="block text-slate-600 mb-1 font-bold">Họ và tên</label>
                <input type="text" required maxLength={100} value={profileForm.fullName}
                  onChange={(e) => setProfileForm({ ...profileForm, fullName: e.target.value })}
                  className="w-full bg-white border border-slate-300 rounded-md px-3 py-2 text-slate-800 focus:outline-none focus:border-blue-500" />
              </div>
              <div>
                <label className="block text-slate-600 mb-1 font-bold">Số điện thoại</label>
                <input type="tel" maxLength={20} value={profileForm.phoneNumber}
                  onChange={(e) => setProfileForm({ ...profileForm, phoneNumber: e.target.value })}
                  placeholder="Có thể để trống"
                  className="w-full bg-white border border-slate-300 rounded-md px-3 py-2 text-slate-800 focus:outline-none focus:border-blue-500" />
              </div>
              <div>
                <label className="block text-slate-600 mb-1 font-bold">Địa chỉ</label>
                <textarea rows={2} maxLength={500} value={profileForm.address}
                  onChange={(e) => setProfileForm({ ...profileForm, address: e.target.value })}
                  className="w-full resize-y bg-white border border-slate-300 rounded-md px-3 py-2 text-slate-800 focus:outline-none focus:border-blue-500" />
              </div>
              <button type="submit" disabled={profileLoading}
                className="w-full py-2.5 bg-blue-600 hover:bg-blue-700 text-white font-bold text-xs rounded-md transition">
                {profileLoading ? 'Đang lưu...' : 'Lưu thông tin'}
              </button>
            </form>

            <form onSubmit={handleProfilePasswordSubmit} className="mt-5 pt-4 border-t border-slate-200 space-y-3 text-xs">
              <h3 className="font-black text-slate-800">Đổi mật khẩu</h3>
              <input type="password" required autoComplete="current-password" placeholder="Mật khẩu hiện tại"
                value={profilePasswordForm.currentPassword}
                onChange={(e) => setProfilePasswordForm({ ...profilePasswordForm, currentPassword: e.target.value })}
                className="w-full bg-white border border-slate-300 rounded-md px-3 py-2 text-slate-800 focus:outline-none focus:border-blue-500" />
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                <input type="password" required minLength={8} autoComplete="new-password" placeholder="Mật khẩu mới (từ 8 ký tự)"
                  value={profilePasswordForm.newPassword}
                  onChange={(e) => setProfilePasswordForm({ ...profilePasswordForm, newPassword: e.target.value })}
                  className="w-full bg-white border border-slate-300 rounded-md px-3 py-2 text-slate-800 focus:outline-none focus:border-blue-500" />
                <input type="password" required minLength={8} autoComplete="new-password" placeholder="Xác nhận mật khẩu mới"
                  value={profilePasswordForm.confirmPassword}
                  onChange={(e) => setProfilePasswordForm({ ...profilePasswordForm, confirmPassword: e.target.value })}
                  className="w-full bg-white border border-slate-300 rounded-md px-3 py-2 text-slate-800 focus:outline-none focus:border-blue-500" />
              </div>
              <button type="submit" disabled={profilePasswordLoading}
                className="w-full py-2.5 bg-slate-800 hover:bg-slate-900 text-white font-bold text-xs rounded-md transition">
                {profilePasswordLoading ? 'Đang cập nhật...' : 'Cập nhật mật khẩu'}
              </button>
            </form>

            <button type="button" onClick={handleLogout}
              className="w-full mt-3 py-2.5 bg-red-50 hover:bg-red-100 text-red-700 font-bold text-xs rounded-md transition">
              Đăng xuất
            </button>
          </div>
        </div>
      )}

      {/* 7. MODAL ĐĂNG KÝ & ĐĂNG NHẬP */}
      {isAuthOpen && (
        <div className="fixed inset-0 z-50 bg-black/60 backdrop-blur-xs flex items-center justify-center p-4">
          <div className="relative w-full max-w-[380px] rounded-2xl p-6 bg-white border border-slate-200 shadow-2xl text-slate-800">
            
            <button onClick={() => setIsAuthOpen(false)} className="absolute top-4 right-4 text-slate-400 hover:text-slate-600">
              <X className="w-5 h-5" />
            </button>

            <div className="text-center mb-5">
              <div className="w-12 h-12 rounded-full bg-blue-50 text-blue-600 flex items-center justify-center mx-auto mb-2">
                <User className="w-6 h-6" />
              </div>
              <h2 className="text-lg font-black text-slate-900">
                {authTab === 'signin' ? 'Đăng Nhập Tài Khoản' : authTab === 'signup' ? 'Đăng Ký Tài Khoản Mới' : authTab === 'forgot' ? 'Quên Mật Khẩu' : 'Đặt Lại Mật Khẩu'}
              </h2>
              {authTab === 'signin' || authTab === 'signup' ? (
                <div className="flex justify-center gap-3 mt-1 text-xs">
                  <button
                    onClick={() => { setAuthTab('signin'); setAuthError(''); setAuthSuccess(''); }}
                    className={`font-bold pb-1 transition ${authTab === 'signin' ? 'text-blue-600 border-b-2 border-blue-600' : 'text-slate-400'}`}
                  >
                    Đăng Nhập
                  </button>
                  <button
                    onClick={() => { setAuthTab('signup'); setAuthError(''); setAuthSuccess(''); }}
                    className={`font-bold pb-1 transition ${authTab === 'signup' ? 'text-blue-600 border-b-2 border-blue-600' : 'text-slate-400'}`}
                  >
                    Đăng Ký
                  </button>
                </div>
              ) : (
                <button
                  type="button"
                  onClick={() => { setAuthTab('signin'); setAuthError(''); setAuthSuccess(''); setResetToken(''); }}
                  className="flex items-center justify-center mx-auto mt-1 text-xs font-bold text-blue-600 hover:text-blue-700"
                >
                  <ChevronLeft className="w-3.5 h-3.5" /> Quay lại đăng nhập
                </button>
              )}
            </div>

            {authError && <div className="mb-3 p-2.5 bg-red-50 border border-red-200 text-red-700 text-xs rounded-xl">{authError}</div>}
            {authSuccess && <div className="mb-3 p-2.5 bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs rounded-xl">{authSuccess}</div>}

            <form onSubmit={handleAuthSubmit} className="space-y-3">
              {authTab === 'signup' && (
                <input
                  type="text"
                  required
                  placeholder="Họ và tên"
                  value={authForm.fullName}
                  onChange={(e) => setAuthForm({ ...authForm, fullName: e.target.value })}
                  className="w-full bg-slate-50 border border-slate-200 rounded-xl px-3.5 py-2 text-xs focus:outline-none focus:border-blue-500"
                />
              )}

              <input
                type="email"
                required
                placeholder="Địa chỉ Email"
                value={authForm.email}
                onChange={(e) => setAuthForm({ ...authForm, email: e.target.value })}
                className="w-full bg-slate-50 border border-slate-200 rounded-xl px-3.5 py-2 text-xs focus:outline-none focus:border-blue-500"
              />

              {authTab === 'reset' && (
                <div className="space-y-1">
                  <label className="text-[11px] font-bold text-slate-600">Mã đặt lại (Development)</label>
                  <input
                    type="text"
                    readOnly
                    value={resetToken}
                    className="w-full bg-slate-100 border border-slate-200 rounded-xl px-3.5 py-2 text-[10px] font-mono text-slate-700"
                  />
                </div>
              )}

              {authTab !== 'forgot' && (
                <div className="relative">
                  <input
                    type={showPassword ? 'text' : 'password'}
                    required
                    minLength={authTab === 'reset' ? 8 : 1}
                    placeholder={authTab === 'reset' ? 'Mật khẩu mới (ít nhất 8 ký tự)' : 'Mật khẩu'}
                    value={authForm.password}
                    onChange={(e) => setAuthForm({ ...authForm, password: e.target.value })}
                    className="w-full bg-slate-50 border border-slate-200 rounded-xl px-3.5 py-2 text-xs focus:outline-none focus:border-blue-500 pr-10"
                  />
                  <button
                    type="button"
                    onClick={() => setShowPassword(!showPassword)}
                    className="absolute right-3 top-2.5 text-slate-400 hover:text-slate-600"
                  >
                    {showPassword ? <EyeOff className="w-4 h-4" /> : <Eye className="w-4 h-4" />}
                  </button>
                </div>
              )}

              {(authTab === 'signup' || authTab === 'reset') && (
                <input
                  type="password"
                  required
                  minLength={authTab === 'reset' ? 8 : 1}
                  placeholder={authTab === 'reset' ? 'Xác nhận mật khẩu mới' : 'Xác nhận mật khẩu'}
                  value={authForm.confirmPassword}
                  onChange={(e) => setAuthForm({ ...authForm, confirmPassword: e.target.value })}
                  className="w-full bg-slate-50 border border-slate-200 rounded-xl px-3.5 py-2 text-xs focus:outline-none focus:border-blue-500"
                />
              )}

              <button
                type="submit"
                disabled={authLoading}
                className="w-full py-2.5 bg-blue-600 hover:bg-blue-700 text-white font-bold text-xs rounded-xl shadow-md transition uppercase mt-2"
              >
                {authLoading ? <Loader2 className="w-4 h-4 animate-spin mx-auto" /> : authTab === 'signin' ? 'Đăng Nhập' : authTab === 'signup' ? 'Tạo Tài Khoản' : authTab === 'forgot' ? 'Gửi yêu cầu' : 'Đặt lại mật khẩu'}
              </button>
              {authTab === 'signin' && (
                <button
                  type="button"
                  onClick={() => { setAuthTab('forgot'); setAuthError(''); setAuthSuccess(''); }}
                  className="w-full text-center text-xs font-semibold text-blue-600 hover:text-blue-700"
                >
                  Quên mật khẩu?
                </button>
              )}
            </form>
          </div>
        </div>
      )}

      {/* ===== THANH SO SÁNH NỔI ===== */}
      {compareList.length > 0 && (
        <div className="fixed bottom-0 left-0 right-0 z-40 shadow-2xl">
          {/* Thông báo lỗi */}
          {compareError && (
            <div className="bg-red-600 text-white text-center text-xs font-bold py-2 px-4 flex items-center justify-center gap-2">
              <AlertTriangle className="w-3.5 h-3.5 shrink-0" />
              {compareError}
            </div>
          )}
          <div className="bg-white border-t-2 border-purple-500">
            <div className="max-w-7xl mx-auto px-4 py-3 flex items-center gap-3 flex-wrap">
              {/* Label */}
              <div className="flex items-center gap-2 shrink-0">
                <Scale className="w-4 h-4 text-purple-600" />
                <span className="text-xs font-black text-slate-800 uppercase">So sánh</span>
                <span className="bg-purple-600 text-white text-[10px] font-black px-1.5 py-0.5 rounded-full">
                  {compareList.length}/3
                </span>
              </div>

              {/* Danh sách sản phẩm đang so sánh */}
              <div className="flex items-center gap-2 flex-1 overflow-x-auto">
                {compareList.map((p, idx) => (
                  <div key={p.id} className="flex items-center gap-1.5 bg-purple-50 border border-purple-200 rounded-lg px-2.5 py-1.5 shrink-0">
                    <div className="w-5 h-5 flex items-center justify-center">
                      {renderIcon(p.categoryType)}
                    </div>
                    <span className="text-[11px] font-bold text-slate-700 max-w-[120px] truncate">{p.name}</span>
                    <span className="text-[10px] font-black text-red-600 shrink-0">{p.price?.toLocaleString('vi-VN')}đ</span>
                    <button
                      onClick={() => setCompareList(prev => prev.filter(x => x.id !== p.id))}
                      className="text-slate-400 hover:text-red-500 transition shrink-0"
                    >
                      <X className="w-3 h-3" />
                    </button>
                  </div>
                ))}

                {/* Slot trống */}
                {Array.from({ length: 3 - compareList.length }).map((_, i) => (
                  <div key={`empty-${i}`} className="flex items-center gap-1.5 bg-slate-50 border border-dashed border-slate-300 rounded-lg px-3 py-1.5 shrink-0">
                    <Plus className="w-3 h-3 text-slate-300" />
                    <span className="text-[11px] text-slate-300 font-medium">Thêm sản phẩm</span>
                  </div>
                ))}
              </div>

              {/* Nút hành động */}
              <div className="flex items-center gap-2 shrink-0">
                <button
                  onClick={() => setCompareList([])}
                  className="px-3 py-1.5 text-[11px] font-bold text-slate-500 hover:text-slate-700 border border-slate-200 rounded-lg transition"
                >
                  Xóa tất cả
                </button>
                <button
                  onClick={() => compareList.length >= 2 && setIsCompareOpen(true)}
                  disabled={compareList.length < 2}
                  className={`px-4 py-1.5 text-[11px] font-black rounded-lg transition flex items-center gap-1.5 ${
                    compareList.length >= 2
                      ? 'bg-purple-600 hover:bg-purple-700 text-white shadow-md'
                      : 'bg-slate-100 text-slate-400 cursor-not-allowed'
                  }`}
                >
                  <BarChart2 className="w-3.5 h-3.5" />
                  So sánh ngay {compareList.length < 2 && `(cần thêm ${2 - compareList.length})`}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ===== MODAL SO SÁNH CHI TIẾT ===== */}
      {isCompareOpen && compareList.length >= 2 && (
        <div className="fixed inset-0 z-50 bg-black/70 backdrop-blur-sm flex items-start justify-center p-4 overflow-y-auto">
          <div className="bg-white rounded-2xl w-full max-w-5xl shadow-2xl my-4">

            {/* Header Modal */}
            <div className="flex items-center justify-between p-5 border-b border-slate-200 bg-gradient-to-r from-purple-600 to-purple-500 rounded-t-2xl">
              <div className="flex items-center gap-3">
                <Scale className="w-5 h-5 text-white" />
                <div>
                  <h2 className="text-sm font-black text-white uppercase">So Sánh Linh Kiện</h2>
                  <p className="text-[11px] text-purple-200">
                    {categories.find(c => c.type === compareList[0].categoryType)?.name || compareList[0].categoryType}
                    {' · '}{compareList.length} sản phẩm
                  </p>
                </div>
              </div>
              <button onClick={() => setIsCompareOpen(false)} className="text-white hover:text-purple-200 transition">
                <X className="w-5 h-5" />
              </button>
            </div>

            {/* Bảng so sánh */}
            <div className="overflow-x-auto">
              <table className="w-full text-xs">
                {/* Header: Ảnh + Tên sản phẩm */}
                <thead>
                  <tr className="border-b border-slate-200">
                    <th className="text-left p-4 text-[11px] font-black text-slate-500 uppercase tracking-wider w-36 bg-slate-50">
                      Thông số
                    </th>
                    {compareList.map((p, idx) => (
                      <th key={p.id} className={`p-4 text-center ${idx === 0 ? 'bg-blue-50' : idx === 1 ? 'bg-purple-50' : 'bg-emerald-50'}`}>
                        <div className="flex flex-col items-center gap-2">
                          <div className="w-16 h-16 bg-white rounded-xl border border-slate-200 flex items-center justify-center shadow-sm">
                            {p.imageUrl?.startsWith('http')
                              ? <img src={p.imageUrl} alt={p.name} className="w-12 h-12 object-contain" />
                              : <div className="scale-150">{renderIcon(p.categoryType)}</div>
                            }
                          </div>
                          <div>
                            <p className="font-black text-slate-800 text-[11px] text-center leading-tight max-w-[160px] line-clamp-2">{p.name}</p>
                            <p className="text-[10px] font-bold text-blue-600 mt-0.5">{p.brand}</p>
                            <p className="text-red-600 font-black text-sm mt-1">{p.price?.toLocaleString('vi-VN')} đ</p>
                          </div>
                          <div className="flex gap-1.5">
                            <button
                              onClick={() => { handleSelectPart(p); setIsCompareOpen(false); setActiveTab('builder'); }}
                              className="px-2.5 py-1 bg-blue-600 text-white text-[10px] font-bold rounded-lg hover:bg-blue-700 transition"
                            >
                              Chọn Ráp
                            </button>
                            <button
                              onClick={() => setCompareList(prev => prev.filter(x => x.id !== p.id))}
                              className="px-2 py-1 bg-slate-100 text-slate-600 text-[10px] font-bold rounded-lg hover:bg-red-50 hover:text-red-600 transition"
                            >
                              <Minus className="w-3 h-3" />
                            </button>
                          </div>
                        </div>
                      </th>
                    ))}
                  </tr>
                </thead>

                <tbody className="divide-y divide-slate-100">
                  {/* Helper render row */}
                  {(() => {
                    const Row = ({ label, values, highlight, unit = '', isPrice = false, isBetter = 'higher' }) => {
                      // Tìm best value để highlight
                      const numVals = values.map(v => parseFloat(String(v).replace(/[^\d.]/g, '')) || 0);
                      const bestVal = isBetter === 'higher' ? Math.max(...numVals) : Math.min(...numVals.filter(v => v > 0));
                      const hasVariation = new Set(values.map(v => v ?? 'N/A')).size > 1;

                      return (
                        <tr className="hover:bg-slate-50/50">
                          <td className="p-3 font-bold text-slate-500 bg-slate-50 text-[11px] uppercase tracking-wide">{label}</td>
                          {values.map((val, idx) => {
                            const numVal = parseFloat(String(val).replace(/[^\d.]/g, '')) || 0;
                            const isBest = hasVariation && val !== null && val !== undefined && val !== '' && val !== 'N/A'
                              && numVal === bestVal && numVals.some(v => v !== bestVal);
                            return (
                              <td key={idx} className={`p-3 text-center font-bold ${
                                isBest ? 'text-emerald-600 bg-emerald-50' : 'text-slate-700'
                              } ${idx === 0 ? 'bg-blue-50/30' : idx === 1 ? 'bg-purple-50/30' : 'bg-emerald-50/30'}`}>
                                {val !== null && val !== undefined && val !== ''
                                  ? <span className="flex items-center justify-center gap-1">
                                      {isPrice ? val?.toLocaleString('vi-VN') : val}{unit}
                                      {isBest && <span className="text-[9px] bg-emerald-500 text-white px-1 py-0.5 rounded font-black">TỐT</span>}
                                    </span>
                                  : <span className="text-slate-300">—</span>
                                }
                              </td>
                            );
                          })}
                        </tr>
                      );
                    };

                    const cat = compareList[0].categoryType;
                    const rows = [];

                    // Hàng chung cho tất cả danh mục
                    rows.push(
                      <Row key="brand" label="Thương hiệu" values={compareList.map(p => p.brand)} isBetter="none" />,
                      <Row key="price" label="Giá bán" values={compareList.map(p => p.price)} isPrice={true} isBetter="lower" unit=" đ" />
                    );

                    // CPU
                    if (cat === 'CPU') {
                      rows.push(
                        <Row key="socket" label="Socket" values={compareList.map(p => p.socket)} isBetter="none" />,
                        <Row key="cores" label="Số nhân" values={compareList.map(p => p.additionalSpecs?.coreCount || '')} unit=" nhân" isBetter="higher" />,
                        <Row key="threads" label="Số luồng" values={compareList.map(p => p.additionalSpecs?.threadCount || '')} unit=" luồng" isBetter="higher" />,
                        <Row key="base" label="Xung cơ bản" values={compareList.map(p => p.additionalSpecs?.baseClock || '')} isBetter="none" />,
                        <Row key="boost" label="Xung boost" values={compareList.map(p => p.additionalSpecs?.boostClock || '')} isBetter="none" />,
                        <Row key="cache" label="Bộ nhớ đệm" values={compareList.map(p => p.additionalSpecs?.cache || '')} isBetter="none" />,
                        <Row key="tdp" label="TDP (công suất)" values={compareList.map(p => p.tdpWattage || '')} unit="W" isBetter="lower" />
                      );
                    }

                    // Mainboard
                    if (cat === 'Mainboard') {
                      rows.push(
                        <Row key="socket" label="Socket" values={compareList.map(p => p.socket)} isBetter="none" />,
                        <Row key="chipset" label="Chipset" values={compareList.map(p => p.chipset)} isBetter="none" />,
                        <Row key="ramtype" label="Chuẩn RAM" values={compareList.map(p => p.ramType)} isBetter="none" />,
                        <Row key="formfactor" label="Form Factor" values={compareList.map(p => p.formFactor)} isBetter="none" />,
                        <Row key="maxram" label="RAM tối đa" values={compareList.map(p => p.additionalSpecs?.maxMemoryGb || '')} unit=" GB" isBetter="higher" />,
                        <Row key="m2" label="Khe M.2" values={compareList.map(p => p.additionalSpecs?.m2Slots ?? '')} unit=" khe" isBetter="higher" />
                      );
                    }

                    // RAM
                    if (cat === 'RAM') {
                      rows.push(
                        <Row key="ramtype" label="Chuẩn RAM" values={compareList.map(p => p.ramType)} isBetter="none" />,
                        <Row key="bus" label="Tốc độ bus" values={compareList.map(p => p.additionalSpecs?.busSpeed || p.additionalSpecs?.ramBusSpeed || '')} unit=" MHz" isBetter="higher" />,
                        <Row key="cap" label="Dung lượng" values={compareList.map(p => p.additionalSpecs?.capacityGb || '')} unit=" GB" isBetter="higher" />,
                        <Row key="modules" label="Số thanh" values={compareList.map(p => p.additionalSpecs?.moduleCount || '')} isBetter="none" />
                      );
                    }

                    // GPU
                    if (cat === 'GPU') {
                      rows.push(
                        <Row key="vram" label="VRAM" values={compareList.map(p => p.additionalSpecs?.vramGb || '')} unit=" GB" isBetter="higher" />,
                        <Row key="tdp" label="Công suất (TDP)" values={compareList.map(p => p.tdpWattage || '')} unit="W" isBetter="lower" />,
                        <Row key="psu" label="Nguồn khuyến nghị" values={compareList.map(p => p.recommendedPsu || p.additionalSpecs?.recommendedPsu || '')} unit="W" isBetter="lower" />,
                        <Row key="len" label="Chiều dài card" values={compareList.map(p => p.lengthMm || p.additionalSpecs?.lengthMm || '')} unit=" mm" isBetter="none" />
                      );
                    }

                    // SSD
                    if (cat === 'SSD') {
                      rows.push(
                        <Row key="cap" label="Dung lượng" values={compareList.map(p => p.additionalSpecs?.capacityGb || '')} unit=" GB" isBetter="higher" />,
                        <Row key="iface" label="Giao tiếp" values={compareList.map(p => p.additionalSpecs?.interfaceType || '')} isBetter="none" />,
                        <Row key="read" label="Tốc độ đọc" values={compareList.map(p => p.additionalSpecs?.readSpeed || '')} unit=" MB/s" isBetter="higher" />,
                        <Row key="write" label="Tốc độ ghi" values={compareList.map(p => p.additionalSpecs?.writeSpeed || '')} unit=" MB/s" isBetter="higher" />
                      );
                    }

                    // PSU
                    if (cat === 'PSU') {
                      rows.push(
                        <Row key="watt" label="Công suất" values={compareList.map(p => p.tdpWattage || '')} unit="W" isBetter="higher" />,
                        <Row key="eff" label="Hiệu suất" values={compareList.map(p => p.additionalSpecs?.efficiencyRating || '')} isBetter="none" />,
                        <Row key="mod" label="Kiểu cáp" values={compareList.map(p => p.additionalSpecs?.modularType || '')} isBetter="none" />
                      );
                    }

                    // Cooler
                    if (cat === 'Cooler') {
                      rows.push(
                        <Row key="type" label="Loại tản" values={compareList.map(p => p.additionalSpecs?.coolingType || '')} isBetter="none" />,
                        <Row key="socket" label="Socket hỗ trợ" values={compareList.map(p => p.socket || '')} isBetter="none" />,
                        <Row key="tdp" label="TDP hỗ trợ" values={compareList.map(p => p.tdpWattage || '')} unit="W" isBetter="higher" />,
                        <Row key="rad" label="Kích thước radiator" values={compareList.map(p => p.additionalSpecs?.radiatorSizeMm || '')} unit=" mm" isBetter="none" />
                      );
                    }

                    // Case
                    if (cat === 'Case') {
                      rows.push(
                        <Row key="ff" label="Form Factor hỗ trợ" values={compareList.map(p => p.formFactor || p.additionalSpecs?.formFactor || '')} isBetter="none" />,
                        <Row key="gpu" label="GPU tối đa" values={compareList.map(p => p.additionalSpecs?.maxGpuLengthMm || '')} unit=" mm" isBetter="higher" />,
                        <Row key="cool" label="Tản khí tối đa" values={compareList.map(p => p.additionalSpecs?.maxCpuCoolerHeightMm || '')} unit=" mm" isBetter="higher" />
                      );
                    }

                    return rows;
                  })()}
                </tbody>
              </table>
            </div>

            {/* Footer modal */}
            <div className="p-4 border-t border-slate-200 bg-slate-50 rounded-b-2xl flex items-center justify-between">
              <p className="text-[11px] text-slate-400">
                <span className="inline-block w-2 h-2 bg-emerald-500 rounded-full mr-1"></span>
                Màu xanh = Giá trị tốt hơn trong bộ so sánh
              </p>
              <div className="flex gap-2">
                <button
                  onClick={() => { setCompareList([]); setIsCompareOpen(false); }}
                  className="px-4 py-2 text-xs font-bold text-slate-600 border border-slate-200 rounded-lg hover:bg-slate-100 transition"
                >
                  Xóa & Đóng
                </button>
                <button
                  onClick={() => setIsCompareOpen(false)}
                  className="px-4 py-2 text-xs font-bold bg-purple-600 text-white rounded-lg hover:bg-purple-700 transition"
                >
                  Đóng
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* 9. FOOTER CHUẨN THƯƠNG MẠI ĐIỆN TỬ */}
      <footer className="bg-white border-t border-slate-200 text-slate-500 text-xs mt-auto">
        <div className="max-w-7xl mx-auto px-4 py-6 grid grid-cols-2 md:grid-cols-4 gap-6 border-b border-slate-100">
          <div className="space-y-2">
            <h4 className="font-bold text-slate-900 uppercase text-xs">Về Chúng Tôi</h4>
            <p className="text-[11px] leading-relaxed">Hệ thống bán lẻ linh kiện máy tính, PC Gaming và thiết bị công nghệ chính hãng hàng đầu tại Việt Nam.</p>
          </div>
          <div className="space-y-2">
            <h4 className="font-bold text-slate-900 uppercase text-xs">Chính Sách & Hỗ Trợ</h4>
            <ul className="space-y-1 text-[11px]">
              <li>• Chính sách bảo hành 1 đổi 1</li>
              <li>• Hướng dẫn mua hàng online</li>
              <li>• Chính sách giao hàng & lắp ráp</li>
            </ul>
          </div>
          <div className="space-y-2">
            <h4 className="font-bold text-slate-900 uppercase text-xs">Hệ Thống Showroom</h4>
            <p className="text-[11px] leading-relaxed">Showroom Đà Nẵng: 123 Nguyễn Văn Linh, Q. Hải Châu</p>
            <p className="text-[11px] leading-relaxed">Showroom Hà Nội: 17 Phố Huế, Q. Hoàn Kiếm</p>
          </div>
          <div className="space-y-2">
            <h4 className="font-bold text-slate-900 uppercase text-xs">Tổng Đài Trợ Giúp</h4>
            <p className="text-red-600 font-bold text-sm">1900 6868</p>
            <p className="text-[11px]">Phục vụ từ 8h00 - 21h30 tất cả các ngày trong tuần</p>
          </div>
        </div>

        <div className="max-w-7xl mx-auto px-4 py-4 text-center text-[11px] text-slate-400">
          © 2026 PC STORE VIETNAM. Bản quyền thuộc về hệ thống PCSTORE.
        </div>
      </footer>

      {/* 10. LỐI TẮT BUILD PC AI */}
      <button
        onClick={() => setActiveTab('ai')}
        className="fixed bottom-6 right-6 z-40 bg-gradient-to-r from-amber-400 to-amber-300 text-slate-900 px-4 h-12 rounded-full shadow-2xl flex items-center gap-2 hover:scale-105 transition-transform group"
      >
        <Sparkles className="w-4 h-4 group-hover:animate-pulse" />
        <span className="text-[10px] font-black tracking-wide">BUILD PC AI</span>
      </button>

      {cartNotice && (
        <div role="status" aria-live="polite" className="fixed bottom-20 right-4 z-50 max-w-sm rounded-xl border border-slate-200 bg-white px-4 py-3 text-xs font-bold text-slate-800 shadow-xl">
          {cartNotice}
          {cartItems.length > 0 && (
            <button type="button" onClick={() => setActiveTab('cart')} className="ml-2 text-blue-600 underline">
              Xem giỏ hàng
            </button>
          )}
        </div>
      )}

      {isStaffPanelOpen && currentUser?.role === 'Staff' && (
        <StaffPanel
          onClose={() => setIsStaffPanelOpen(false)}
          staffName={currentUser.fullName}
        />
      )}
      {isAdminPanelOpen && ['Admin', 'Manager'].includes(currentUser?.role) && (
        <AdminPanel
          onClose={() => setIsAdminPanelOpen(false)}
          staffName={currentUser.fullName}
        />
      )}

    </div>
  );
}