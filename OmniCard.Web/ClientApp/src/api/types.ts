// Mirrors OmniCard.Api.Contracts DTOs. Kept in sync by hand for now; can be replaced with a
// generated client from /openapi/v1.json later.

export interface PagedResult<T> {
  total: number;
  skip: number;
  take: number;
  items: T[];
}

export interface GameDto {
  id: string;
  displayName: string;
}

export interface CardDto {
  id: number;
  game: string;
  gameCardId: string;
  name: string;
  setName: string;
  setCode: string;
  number: string;
  rarity: string;
  imageUri?: string | null;
  scanImagePath?: string | null;
  condition: string;
  isFoil: boolean;
  foilType?: string | null;
  quantity: number;
  tags: string[];
  purchasePrice?: number | null;
  marketPrice: number;
  containerId?: number | null;
  containerName?: string | null;
  page?: number | null;
  slot?: number | null;
  section?: string | null;
  color?: string | null;
  cardType?: string | null;
  isMissing: boolean;
  isTraded: boolean;
}

export interface LocationSummaryDto {
  id: number;
  name: string;
  type: string;
  isSystem: boolean;
  isAlwaysAvailable: boolean;
  cardCount: number;
  uniquePrintCount: number;
  totalMarketValue: number;
  totalPurchaseCost: number;
  priceDelta: number;
  priceDeltaPercent: number;
  coverImageUri?: string | null;
}

export interface ValuationLineDto {
  key: string;
  units: number;
  cost: number;
  market: number;
}

export interface RealizedDto {
  totalSold: number;
  totalProceeds: number;
  totalCost: number;
  totalFees: number;
  profit: number;
}

export interface DashboardDto {
  totalUnits: number;
  totalCost: number;
  totalMarket: number;
  unrealizedDelta: number;
  byGame: ValuationLineDto[];
  byCategory: ValuationLineDto[];
  byLocation: ValuationLineDto[];
  realized: RealizedDto;
}

export interface AuthStatusDto {
  authRequired: boolean;
  authenticated: boolean;
}

export interface SetInfoDto {
  setCode: string;
  setName: string;
}

export interface SetChecklistCardDto {
  gameCardId: string;
  collectorNumber: string;
  name: string;
  rarity: string;
  imageUri?: string | null;
  ownedQuantity: number;
  normalPrice?: number | null;
  foilPrice?: number | null;
  hasFoil: boolean;
}

export interface SetChecklistDto {
  game: string;
  setCode: string;
  setName: string;
  ownedCount: number;
  totalCount: number;
  ownedPhysicalCount: number;
  completionPercent: number;
  cards: SetChecklistCardDto[];
}

// --- Binder (matches OmniCard.Web.Services.BinderStateDto) ---

export interface BinderCardDto {
  id: number;
  game: number;
  name: string;
  setName: string;
  setCode: string;
  number: string;
  rarity: string;
  color?: string | null;
  cardType?: string | null;
  foil: boolean;
  foilType?: string | null;
  condition: string;
  purchasePrice?: number | null;
  price?: string | null;
  marketPriceRaw: number;
  imageUrl?: string | null;
  isTraded: boolean;
  tags: string[];
  page?: number | null;
  slot?: number | null;
  containerId?: number | null;
  tcgPlayerUrl: string;
}

export interface BinderSlotDto {
  slotIndex: number;
  card: BinderCardDto | null;
  reverseGame?: number | null;
}

export interface SpreadTabDto {
  index: number;
  label: string;
  isCurrent: boolean;
}

export interface BinderStateDto {
  containerName: string;
  slotsPerPage: number;
  columns: number;
  totalPages: number;
  spreadIndex: number;
  totalSpreads: number;
  leftPageNumber?: number | null;
  rightPageNumber?: number | null;
  pageRangeLabel: string;
  leftSlots: BinderSlotDto[];
  rightSlots: BinderSlotDto[];
  spreadTabs: SpreadTabDto[];
}
