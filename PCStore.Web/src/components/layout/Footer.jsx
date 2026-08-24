import React from 'react';
import { Cpu, ShieldCheck, Truck, RotateCcw, Headphones } from 'lucide-react';

export default function Footer() {
  return (
    <footer className="bg-slate-950 border-t border-slate-800 text-slate-400 text-sm mt-auto">
      <div className="max-w-7xl mx-auto px-4 py-8 border-b border-slate-800/80 grid grid-cols-2 md:grid-cols-4 gap-6">
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-cyan-500/10 text-cyan-400"><ShieldCheck className="w-6 h-6" /></div>
          <div>
            <h4 className="font-bold text-white text-sm">100% Chính Hãng</h4>
            <p className="text-xs text-slate-500">Bảo hành 36 tháng</p>
          </div>
        </div>
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-cyan-500/10 text-cyan-400"><Truck className="w-6 h-6" /></div>
          <div>
            <h4 className="font-bold text-white text-sm">Giao Toàn Quốc</h4>
            <p className="text-xs text-slate-500">Miễn phí ship đơn máy bộ</p>
          </div>
        </div>
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-cyan-500/10 text-cyan-400"><RotateCcw className="w-6 h-6" /></div>
          <div>
            <h4 className="font-bold text-white text-sm">Đổi Trả 7 Ngày</h4>
            <p className="text-xs text-slate-500">Lỗi phần cứng 1 đổi 1</p>
          </div>
        </div>
        <div className="flex items-center gap-3">
          <div className="p-2.5 rounded-xl bg-cyan-500/10 text-cyan-400"><Headphones className="w-6 h-6" /></div>
          <div>
            <h4 className="font-bold text-white text-sm">AI Tư Vấn 24/7</h4>
            <p className="text-xs text-slate-500">Check tương thích tự động</p>
          </div>
        </div>
      </div>

      <div className="max-w-7xl mx-auto px-4 py-6 flex flex-col sm:flex-row items-center justify-between gap-4 text-xs">
        <div className="flex items-center gap-2">
          <Cpu className="w-4 h-4 text-cyan-400" />
          <span className="font-semibold text-slate-300">© 2026 PCSTORE Vietnam. Hệ thống phân phối linh kiện & PC Gaming.</span>
        </div>
        <p className="text-slate-500">Đà Nẵng, Việt Nam • Hotline: 1900 6868</p>
      </div>
    </footer>
  );
}