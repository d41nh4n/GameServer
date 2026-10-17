import { afterEach, describe, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import PzBuildInfo from "./PzBuildInfo";
import type { AuthProvider } from "../../auth";

afterEach(() => cleanup());

describe("PzBuildInfo", () => {
  it("shows installed build and manifest update time", async () => {
    const auth = { pzBuildInfo: vi.fn().mockResolvedValue({ buildId: "123456", lastUpdatedUtc: "2026-10-17T10:00:00Z", error: null }) } as unknown as AuthProvider;
    render(<PzBuildInfo auth={auth} />);
    expect(await screen.findByText("123456")).toBeTruthy();
    expect(screen.getByText("Steam build", { exact: true })).toBeTruthy();
    expect(auth.pzBuildInfo).toHaveBeenCalledOnce();
  });

  it("shows unavailable state instead of a fake build", async () => {
    const auth = { pzBuildInfo: vi.fn().mockResolvedValue({ buildId: null, lastUpdatedUtc: null, error: "Manifest not found" }) } as unknown as AuthProvider;
    render(<PzBuildInfo auth={auth} />);
    expect(await screen.findByText(/Unavailable/i)).toBeTruthy();
    expect(screen.getByText(/Manifest not found/)).toBeTruthy();
  });
});
