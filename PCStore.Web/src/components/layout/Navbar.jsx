import React from 'react';
import { Cpu, ShoppingCart, Wrench, Search } from 'lucide-react';

export default function Navbar({ activeTab, setActiveTab, cartCount = 0 }) {
  return (
    <header className="sticky top-0 z-40 bg-slate-900/90 backdrop-blur-md border-b border-slate-800">
      <div className="max-w-7xl mx-auto px-4 h-16 flex items-center justify-between gap-4">
        <div className="flex items-center gap-2 cursor-pointer select-none" onClick={() => setActiveTab('home')}>
          <div className="p-2 rounded-xl bg-cyan-500/10 border border-cyan-500/30">
            <Cpu className="w-6 h-6 text-cyan-400" />
          </div>
          <div>
            <span className="text-xl font-black tracking-wider bg-gradient-to-r from-cyan-400 to-blue-500 bg-clip-text text-transparent">
              PCSTORE
            </span>
            <span className="hidden sm:inline-block ml-2 text-[10px] font-semibold uppercase px-1.5 py-0.5 rounded bg-cyan-500/20 text-cyan-300">
              AI Powered
            </span>
          </div>
        </div>

        <div className="flex-1 max-w-lg relative hidden md:block">
          <input
            type="text"
            placeholder="Tìm kiếm CPU Intel, Ryzen 7000, RTX 4070..."
            className="w-full bg-slate-950/70 border border-slate-700/80 rounded-xl pl-10 pr-4 py-2 text-sm focus:outline-none focus:border-cyan-400 text-slate-200"
          />
          <Search className="w-4 h-4 text-slate-400 absolute left-3 top-2.5" />
        </div>

        <nav className="flex items-center gap-2 sm:gap-3">
          <button 
            onClick={() => setActiveTab('home')}
            className={`px-3 py-2 rounded-xl text-sm font-semibold transition ${
              activeTab === 'home' ? 'text-cyan-400 bg-slate-800' : 'text-slate-300 hover:text-white'
            }`}
          >
            Trang chủ
          </button>

          <button 
            onClick={() => setActiveTab('builder')}
            className={`flex items-center gap-2 px-4 py-2 rounded-xl text-sm font-semibold transition ${
              activeTab === 'builder' ? 'bg-cyan-500 text-slate-950 font-bold' : 'bg-cyan-500/10 border border-cyan-500/30 text-cyan-400'
            }`}
          >
            <Wrench className="w-4 h-4" />
            <span>Ráp PC</span>
          </button>

          <button className="p-2.5 rounded-xl bg-slate-800/80 hover:bg-slate-700 border border-slate-700/80 text-slate-200 relative">
            <ShoppingCart className="w-5 h-5" />
            {cartCount > 0 && (
              <span className="absolute -top-1.5 -right-1.5 bg-rose-500 text-[11px] text-white rounded-full w-5 h-5 flex items-center justify-center font-bold shadow">
                {cartCount}
              </span>
            )}
          </button>
        </nav>
      </div>
    </header>
  );
}