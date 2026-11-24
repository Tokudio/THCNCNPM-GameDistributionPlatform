import { StoreCardGame, StoreGame, StoreGameDetails } from '../../core/models/game-store.models';

const formatter = new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 });
export const wideArtwork = 'store/artwork-wide.svg';

export function cardArtwork(index: number): string {
  return `store/artwork-${index % 6 + 1}.svg`;
}

export function publicWebUrl(value: string | null): string | null {
  if (!value || !/^https?:\/\//i.test(value)) return null;
  try {
    const url = new URL(value);
    return url.hostname && !url.username && !url.password ? url.href : null;
  } catch {
    return null;
  }
}

export function toCards(games: StoreGame[], details: ReadonlyMap<string, StoreGameDetails | null | undefined>): StoreCardGame[] {
  return games.flatMap(game => {
    const detail = details.get(game.id);
    if (detail === null) return [];
    return [{ ...(detail ?? game), categories: detail?.categories ?? [] }];
  });
}

export function minorUnits(price: string): bigint {
  const [whole, fraction = ''] = price.split('.');
  return BigInt(whole) * 100n + BigInt(fraction.padEnd(2, '0'));
}

function formatAmount(amount: bigint, currency: string): string {
  const cents = amount % 100n;
  const fraction = cents > 0n ? `,${cents.toString().padStart(2, '0')}` : '';
  return `${formatter.format(amount / 100n)}${fraction} ${currency === 'VND' ? '₫' : currency}`;
}

export function formatMoney(price: string, currency: string): string {
  return formatAmount(minorUnits(price), currency);
}

export function gamePrice(game: Pick<StoreGame, 'price' | 'discountPercent' | 'currency'>) {
  const original = minorUnits(game.price);
  const discount = original > 0n ? game.discountPercent : 0;
  // Presentation only. Round the discounted decimal amount to the schema's two decimal places.
  const final = (original * BigInt(100 - discount) + 50n) / 100n;
  return {
    original: discount ? formatAmount(original, game.currency) : null,
    final: final === 0n ? 'Free' : formatAmount(final, game.currency),
    free: final === 0n,
    discount,
  };
}

export function storeError(error: unknown): string {
  const status = typeof error === 'object' && error !== null && 'status' in error ? error.status : null;
  return status === 0 ? 'Check your connection and try again.' : 'The store could not load this content. Try again.';
}
