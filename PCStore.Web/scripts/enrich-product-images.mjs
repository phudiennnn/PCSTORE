const apiBase = (process.env.PCSTORE_API_URL || 'http://localhost:5171').replace(/\/$/, '');
const shouldApply = process.argv.includes('--apply');
const limitArgument = process.argv.find((argument) => argument.startsWith('--limit='));
const skuArgument = process.argv.find((argument) => argument.startsWith('--sku='));
const limit = limitArgument ? Number(limitArgument.split('=')[1]) : Infinity;
const selectedSkus = skuArgument ? new Set(skuArgument.split('=')[1].split(',')) : null;
const concurrency = 3;
const allowedRetailers = [
  'gearvn.com',
  'hacom.vn',
  'phongvu.vn',
  'anphatpc.com.vn',
  'nguyencongpc.vn',
  'memoryzone.com.vn',
  'kccshop.vn',
  'tncstore.vn',
  'hoanghapc.vn',
  'xgear.net',
  'cellphones.com.vn',
  'hanoicomputer.vn',
  'meta.vn',
  'phucanh.vn',
  'tinhocngoisao.com'
];
const ignoredTokens = new Set([
  'cpu', 'ram', 'vga', 'gpu', 'ssd', 'hdd', 'psu', 'case', 'nguon', 'vo', 'may',
  'tan', 'nhiet', 'nuoc', 'khi', 'o', 'cung', 'card', 'man', 'hinh', 'intel',
  'amd', 'nvidia', 'asus', 'gigabyte', 'msi', 'asrock', 'corsair', 'kingston',
  'samsung', 'gskill', 'western', 'digital', 'wd', 'gaming', 'edition',
  'glass', 'tempered', 'wifi', 'with', 'and', 'for', 'the', 'series', 'new', 'gen', 'pcie', 'sata', 'm2',
  'mhz', 'gb', 'tb', 'mm', 'inch', 'w'
]);
const variantTokens = new Set([
  'pro', 'evo', 'plus', 'max', 'super', 'ti', 'xt', 'xtx', 'white', 'black', 'rgb',
  'shift', 'neo', 'lpx', 'hero', 'master', 'lcd', 'airflow', 'ddr4', 'ddr5',
  'dominator', 'vengeance', 'trident', 'flare', 'ripjaws', 'aegis', 'renegade', 'beast',
  'valueram', 'rog', 'strix', 'tuf', 'dual', 'aorus', 'windforce', 'ventus', 'suprim',
  'taichi', 'challenger', 'eagle', 'proart', 'phoenix', 'quadro'
]);

function decodeHtml(value) {
  return value
    .replaceAll('&amp;', '&')
    .replaceAll('&quot;', '"')
    .replaceAll('&#x27;', "'")
    .replaceAll('&#39;', "'")
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>');
}

function normalize(value) {
  return value
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')
    .replaceAll('đ', 'd')
    .replaceAll('Đ', 'd')
    .toLowerCase();
}

function getTokens(value) {
  return normalize(value).match(/[a-z0-9]+/g) || [];
}

function isProductPage(url) {
  const path = new URL(url).pathname.toLowerCase();
  return !/(search|collection|collections|category|categories|compare|review|benchmark|forum|blog|news|tag|pages|login|cart)/.test(path)
    && path.split('/').filter(Boolean).length >= 1;
}

function retailerAllowed(url) {
  const hostname = new URL(url).hostname.toLowerCase();
  return allowedRetailers.some((domain) => hostname === domain || hostname.endsWith(`.${domain}`));
}

function candidateScore(product, title, url) {
  const candidateTokens = new Set(getTokens(`${title} ${url}`));
  const productTokens = getTokens(product.name)
    .filter((token) => !ignoredTokens.has(token) && (token.length > 2 || variantTokens.has(token)));
  const distinctiveTokens = [...new Set(productTokens)];
  const numericTokens = [...new Set(productTokens.filter((token) => /\d/.test(token)))];
  const requestedVariants = new Set(productTokens.filter((token) => variantTokens.has(token)));
  const candidateVariants = new Set([...candidateTokens].filter((token) => variantTokens.has(token)));
  const matchedWords = distinctiveTokens.filter((token) => candidateTokens.has(token)).length;
  const matchedNumbers = numericTokens.filter((token) => candidateTokens.has(token)).length;
  const brandTokens = getTokens(product.brand || '').filter((token) => token.length > 2);
  const brandMatched = brandTokens.length === 0 || brandTokens.some((token) => candidateTokens.has(token));
  const wordScore = distinctiveTokens.length ? matchedWords / distinctiveTokens.length : 0;
  const numberScore = numericTokens.length ? matchedNumbers / numericTokens.length : 0;

  if ([...requestedVariants].some((token) => !candidateVariants.has(token))) return 0;
  if ([...candidateVariants].some((token) => !requestedVariants.has(token))) return 0;
  if (!brandMatched || (numericTokens.length && matchedNumbers === 0)) return 0;
  if (distinctiveTokens.length > 1 && matchedWords < 2) return 0;
  return wordScore * 0.55 + numberScore * 0.45;
}

function unwrapSearchUrl(href) {
  const decodedHref = decodeHtml(href);
  const absoluteHref = decodedHref.startsWith('//') ? `https:${decodedHref}` : decodedHref;
  const url = new URL(absoluteHref, 'https://html.duckduckgo.com');
  return url.searchParams.get('uddg') || url.href;
}

function getSearchCandidates(html, product) {
  const anchors = [...html.matchAll(/class="result__a" href="([^"]+)"[^>]*>(.*?)<\/a>/g)];
  return anchors
    .map((match) => {
      const title = decodeHtml(match[2].replace(/<[^>]*>/g, ''));
      const url = unwrapSearchUrl(match[1]);
      return { title, url, score: candidateScore(product, title, url) };
    })
    .filter((candidate) => candidate.score >= 0.42 && retailerAllowed(candidate.url) && isProductPage(candidate.url))
    .sort((left, right) => right.score - left.score);
}

function getBingCandidates(html, product) {
  const cards = [...html.matchAll(/class="iusc"[^>]*\sm="([^"]+)"/g)];
  return cards
    .flatMap((match) => {
      try {
        const image = JSON.parse(decodeHtml(match[1]));
        const score = candidateScore(product, image.t || image.desc || '', image.purl || '');
        return image.murl && image.purl && isProductPage(image.purl) && score >= 0.42
          ? [{ title: image.t || image.desc || '', url: image.purl || '', imageUrl: image.murl, score }]
          : [];
      } catch {
        return [];
      }
    })
    .sort((left, right) => right.score - left.score);
}

function getMetaContent(html, property) {
  for (const tag of html.matchAll(/<meta\b[^>]*>/gi)) {
    if (!new RegExp(`(?:property|name)=["']${property}["']`, 'i').test(tag[0])) continue;
    const content = tag[0].match(/content=["']([^"']+)["']/i)?.[1];
    if (content) return decodeHtml(content);
  }
  return null;
}

function getProductImage(html, pageUrl) {
  const image = getMetaContent(html, 'og:image') || getMetaContent(html, 'twitter:image');
  if (!image) return null;

  try {
    const imageUrl = new URL(image, pageUrl);
    if (imageUrl.protocol !== 'https:') return null;
    if (/logo|icon|banner|payment|shipping|avatar/i.test(imageUrl.pathname)) return null;
    if (!/\.(png|jpe?g|webp)(?:$|\?)/i.test(imageUrl.href)) return null;
    return imageUrl.href;
  } catch {
    return null;
  }
}

async function lookupImage(product) {
  const bingResponse = await fetch(`https://www.bing.com/images/search?q=${encodeURIComponent(product.name)}`, {
    headers: { 'User-Agent': 'Mozilla/5.0 PCStoreCatalogImageImporter/1.0' }
  });
  if (bingResponse.ok) {
    const bingHtml = await bingResponse.text();
    for (const candidate of getBingCandidates(bingHtml, product).slice(0, 8)) {
      try {
        const imageUrl = new URL(candidate.imageUrl);
        if (imageUrl.protocol !== 'https:' || !/\.(png|jpe?g|webp|avif)(?:$|\?)/i.test(imageUrl.href)) continue;
        if (/logo|icon|banner|payment|shipping|avatar/i.test(imageUrl.pathname)) continue;
        return { sku: product.sku, imageUrl: imageUrl.href, sourceUrl: candidate.url, confidence: candidate.score };
      } catch {
        // Try the next relevant image result.
      }
    }
  }

  const query = `${product.name} product`;
  const searchResponse = await fetch(`https://html.duckduckgo.com/html/?q=${encodeURIComponent(query)}`, {
    headers: { 'User-Agent': 'Mozilla/5.0 PCStoreCatalogImageImporter/1.0' }
  });
  if (!searchResponse.ok) return null;

  const searchHtml = await searchResponse.text();
  const candidates = getSearchCandidates(searchHtml, product).slice(0, 4);
  for (const candidate of candidates) {
    try {
      const response = await fetch(candidate.url, { headers: { 'User-Agent': 'Mozilla/5.0' }, redirect: 'follow' });
      if (!response.ok || !retailerAllowed(response.url)) continue;
      const html = await response.text();
      const imageUrl = getProductImage(html, response.url);
      if (imageUrl) return { sku: product.sku, imageUrl, sourceUrl: response.url, confidence: candidate.score };
    } catch {
      // Try the next product listing.
    }
  }
  return null;
}

async function mapWithConcurrency(items, worker) {
  const results = new Array(items.length);
  let nextIndex = 0;
  await Promise.all(Array.from({ length: concurrency }, async () => {
    while (nextIndex < items.length) {
      const index = nextIndex++;
      try {
        results[index] = await worker(items[index], index);
      } catch (error) {
        results[index] = null;
        console.warn(`Lookup failed for ${items[index].sku}: ${error.message}`);
      }
    }
  }));
  return results;
}

const response = await fetch(`${apiBase}/api/products`);
if (!response.ok) throw new Error(`Product API returned ${response.status}`);
const allProducts = await response.json();
const importedSkuPrefixes = ['CPU-INT-', 'CPU-AMD-', 'MB-', 'VGA-', 'RAM-', 'SSD-', 'HDD-', 'PSU-', 'CAS-', 'COL-'];
let products = allProducts.filter((product) =>
  !product.imageUrl && importedSkuPrefixes.some((prefix) => product.sku.startsWith(prefix))
);
if (selectedSkus) products = products.filter((product) => selectedSkus.has(product.sku));
if (Number.isFinite(limit)) products = products.slice(0, limit);

console.log(`Looking up images for ${products.length} products from ${apiBase}`);
const resolved = await mapWithConcurrency(products, async (product, index) => {
  const result = await lookupImage(product);
  if (result) console.log(`[${index + 1}/${products.length}] ${result.sku} <- ${result.sourceUrl}`);
  else console.log(`[${index + 1}/${products.length}] no confident image for ${product.sku}`);
  return result;
});
const imageUpdates = resolved.filter(Boolean).map(({ sku, imageUrl }) => ({ sku, imageUrl }));
console.log(`Resolved ${imageUpdates.length}/${products.length} product images.`);

if (shouldApply && imageUpdates.length) {
  for (let start = 0; start < imageUpdates.length; start += 100) {
    const batch = imageUpdates.slice(start, start + 100);
    const updateResponse = await fetch(`${apiBase}/api/dev/catalog/product-images`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ images: batch })
    });
    const result = await updateResponse.json();
    if (!updateResponse.ok) throw new Error(`Image import failed: ${JSON.stringify(result)}`);
    console.log(`Saved ${result.updated} product images.`);
    if (result.missingSkus?.length) console.warn(`Missing SKUs: ${result.missingSkus.join(', ')}`);
  }
} else if (!shouldApply) {
  console.log('Dry run only. Pass --apply to save these image URLs.');
}
