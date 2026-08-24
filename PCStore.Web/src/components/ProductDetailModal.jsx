import React, { useEffect, useState } from 'react';
import {
  X, Loader2, AlertCircle, Cpu, Layers, Monitor, HardDrive, Zap, Box, Fan,
  Plus, ShoppingBag
} from 'lucide-react';
import { productService } from '../services/api';

const renderProductIcon = (type) => {
  switch (type) {
    case 'CPU': return <Cpu className="w-10 h-10 text-indigo-600" />;
    case 'Mainboard': return <Layers className="w-10 h-10 text-cyan-600" />;
    case 'RAM': return <HardDrive className="w-10 h-10 text-emerald-600" />;
    case 'GPU': return <Monitor className="w-10 h-10 text-green-600" />;
    case 'SSD': return <HardDrive className="w-10 h-10 text-purple-600" />;
    case 'PSU': return <Zap className="w-10 h-10 text-amber-500" />;
    case 'Case': return <Box className="w-10 h-10 text-slate-700" />;
    case 'Cooler': return <Fan className="w-10 h-10 text-sky-500" />;
    default: return <Cpu className="w-10 h-10 text-indigo-600" />;
  }
};

export default function ProductDetailModal({ productId, isOpen, onClose, onSelectToBuild }) {
  const [product, setProduct] = useState(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    if (!isOpen || !productId) {
      setProduct(null);
      setError('');
      return;
    }

    const loadProduct = async () => {
      try {
        setLoading(true);
        setError('');
        const data = await productService.getProductById(productId);
        setProduct(data);
      } catch (err) {
        setError(err.response?.data?.message || 'Không thể tải chi tiết sản phẩm.');
        setProduct(null);
      } finally {
        setLoading(false);
      }
    };

    loadProduct();
  }, [isOpen, productId]);

  if (!isOpen) return null;

  const specItems = product ? [
    { label: 'Danh mục', value: product.categoryName },
    { label: 'SKU', value: product.sku },
    { label: 'Socket', value: product.socket },
    { label: 'Chipset', value: product.chipset },
    { label: 'Chuẩn RAM', value: product.ramType },
    { label: 'Bus RAM', value: product.ramBusSpeed ? `${product.ramBusSpeed} MHz` : null },
    { label: 'Khe RAM', value: product.ramSlots },
    { label: 'TDP', value: product.tdpWattage > 0 ? `${product.tdpWattage}W` : null },
    { label: 'Nguồn khuyến nghị', value: product.recommendedPsu > 0 ? `${product.recommendedPsu}W` : null },
    { label: 'Form Factor', value: product.formFactor },
  ].filter(item => item.value !== null && item.value !== undefined && item.value !== '') : [];

  return (
    <div className="fixed inset-0 z-50 bg-slate-900/60 backdrop-blur-xs flex items-center justify-center p-4">
      <div className="bg-white border border-slate-200 rounded-3xl max-w-2xl w-full max-h-[90vh] overflow-hidden shadow-2xl flex flex-col">
        <div className="p-4 border-b border-slate-100 flex justify-between items-center bg-slate-50">
          <div>
            <p className="text-[11px] font-bold text-indigo-600 uppercase">Chi tiết sản phẩm</p>
            <h3 className="text-sm font-black text-slate-900">Thông số kỹ thuật từ PostgreSQL</h3>
          </div>
          <button onClick={onClose} className="p-1.5 hover:bg-slate-200 rounded-xl text-slate-500">
            <X className="w-5 h-5" />
          </button>
        </div>

        <div className="p-5 overflow-y-auto flex-1">
          {loading ? (
            <div className="flex flex-col items-center justify-center py-16 space-y-3">
              <Loader2 className="w-8 h-8 text-indigo-600 animate-spin" />
              <p className="text-xs font-semibold text-slate-500">Đang tải chi tiết sản phẩm...</p>
            </div>
          ) : error ? (
            <div className="flex flex-col items-center justify-center py-16 space-y-3 text-center">
              <AlertCircle className="w-10 h-10 text-rose-500" />
              <p className="text-sm font-bold text-rose-700">{error}</p>
            </div>
          ) : product ? (
            <div className="space-y-6">
              <div className="flex flex-col sm:flex-row gap-5">
                <div className="w-full sm:w-44 h-44 bg-slate-50 rounded-2xl border border-slate-100 flex items-center justify-center shrink-0">
                  {product.imageUrl && product.imageUrl.startsWith('http') ? (
                    <img src={product.imageUrl} alt={product.name} className="w-28 h-28 object-contain" />
                  ) : (
                    renderProductIcon(product.categoryType)
                  )}
                </div>

                <div className="flex-1 space-y-3">
                  <div className="flex flex-wrap gap-2">
                    <span className="text-[10px] font-bold text-slate-600 bg-white border border-slate-200 px-2 py-0.5 rounded-md">
                      {product.brand}
                    </span>
                    <span className="text-[10px] font-bold text-indigo-700 bg-indigo-50 px-2 py-0.5 rounded-md uppercase">
                      {product.categoryType}
                    </span>
                  </div>

                  <h2 className="text-base font-black text-slate-900 leading-snug">{product.name}</h2>

                  <div className="flex items-end gap-3">
                    <p className="text-xl font-black text-indigo-600">
                      {product.price?.toLocaleString('vi-VN')} đ
                    </p>
                    <p className="text-xs text-slate-500 pb-0.5">
                      Kho: <strong className="text-slate-800">{product.stockQuantity || 0}</strong> chiếc
                    </p>
                  </div>
                </div>
              </div>

              {specItems.length > 0 && (
                <div className="bg-slate-50 border border-slate-200 rounded-2xl p-4">
                  <h4 className="text-xs font-black text-slate-900 uppercase mb-3">Thông số kỹ thuật</h4>
                  <div className="grid grid-cols-1 sm:grid-cols-2 gap-2">
                    {specItems.map((item) => (
                      <div key={item.label} className="flex justify-between gap-3 text-xs bg-white border border-slate-100 rounded-xl px-3 py-2">
                        <span className="text-slate-500 font-semibold">{item.label}</span>
                        <span className="text-slate-900 font-bold text-right">{item.value}</span>
                      </div>
                    ))}
                  </div>
                </div>
              )}
            </div>
          ) : null}
        </div>

        {product && !loading && !error && (
          <div className="p-4 border-t border-slate-100 bg-white flex flex-col sm:flex-row gap-2">
            <button
              onClick={() => {
                onSelectToBuild?.(product);
                onClose?.();
              }}
              className="flex-1 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white font-bold text-xs rounded-xl transition flex items-center justify-center gap-2"
            >
              <Plus className="w-4 h-4" />
              Thêm vào dàn PC
            </button>
            <button
              onClick={onClose}
              className="flex-1 py-2.5 bg-slate-100 hover:bg-slate-200 text-slate-700 font-bold text-xs rounded-xl transition flex items-center justify-center gap-2"
            >
              <ShoppingBag className="w-4 h-4" />
              Đóng
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
