import { createContext, useContext, useMemo, useState, type ReactNode } from 'react';

/** The app-wide active game filter. `undefined` means "All Games". */
interface GameContextValue {
  game: string | undefined;
  setGame: (game: string | undefined) => void;
}

const GameContext = createContext<GameContextValue | undefined>(undefined);

export function GameProvider({ children }: { children: ReactNode }) {
  const [game, setGame] = useState<string | undefined>(() => {
    return localStorage.getItem('omnicard.game') ?? undefined;
  });

  const value = useMemo<GameContextValue>(
    () => ({
      game,
      setGame: (g) => {
        setGame(g);
        if (g) localStorage.setItem('omnicard.game', g);
        else localStorage.removeItem('omnicard.game');
      },
    }),
    [game],
  );

  return <GameContext.Provider value={value}>{children}</GameContext.Provider>;
}

export function useGame() {
  const ctx = useContext(GameContext);
  if (!ctx) throw new Error('useGame must be used within GameProvider');
  return ctx;
}
