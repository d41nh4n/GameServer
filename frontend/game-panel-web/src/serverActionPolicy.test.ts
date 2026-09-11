import { describe, expect, it } from "vitest";
import { isActionAllowed } from "./serverActionPolicy";

describe("server action policy", () => {
  it("allows only Start when stopped", () => {
    expect(isActionAllowed(0, "start")).toBe(true);
    expect(isActionAllowed(0, "stop")).toBe(false);
    expect(isActionAllowed(0, "restart")).toBe(false);
  });
  it("disables every action while starting", () => {
    for (const action of ["start", "stop", "restart"] as const) expect(isActionAllowed(2, action)).toBe(false);
  });
  it("allows Stop and Restart only when started", () => {
    expect(isActionAllowed(1, "start")).toBe(false);
    expect(isActionAllowed(1, "stop")).toBe(true);
    expect(isActionAllowed(1, "restart")).toBe(true);
  });
  it("disables every action while stopping", () => {
    for (const action of ["start", "stop", "restart"] as const) expect(isActionAllowed(3, action)).toBe(false);
  });
});
