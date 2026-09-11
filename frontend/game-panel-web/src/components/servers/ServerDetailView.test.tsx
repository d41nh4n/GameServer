import { describe, expect, it, vi, afterEach } from "vitest";
import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import type { Server } from "../../auth";
import { DetailControls } from "./ServerDetailView";

afterEach(() => cleanup());

const server = (status: number, ready = false): Server => ({
  id: "server-1", name: "Valheim Main Server", gameType: "Valheim", type: 0,
  status, port: 2456, worldName: "Dedicated", instanceKey: null,
  provisioningMode: "AdoptExisting", runtimeType: "Systemd", ready,
});

function renderControls(status: number, onAction = vi.fn()) {
  render(<DetailControls server={server(status, status === 1)} loading={false} onAction={onAction} />);
  return {
    start: screen.getByRole("button", { name: "▶ Start" }),
    stop: screen.getByRole("button", { name: "■ Stop" }),
    restart: screen.getByRole("button", { name: "↻ Restart" }),
    onAction,
  };
}

describe("DetailControls expected behavior", () => {
  it("Stopped enables only Start and sends start", () => {
    const controls = renderControls(0);
    expect(controls.start).toBeEnabled();
    expect(controls.stop).toBeDisabled();
    expect(controls.restart).toBeDisabled();
    fireEvent.click(controls.start);
    expect(controls.onAction).toHaveBeenCalledWith("server-1", "start");
  });

  it("Starting disables every action", () => {
    const controls = renderControls(2);
    expect(controls.start).toBeDisabled();
    expect(controls.stop).toBeDisabled();
    expect(controls.restart).toBeDisabled();
    fireEvent.click(controls.start);
    fireEvent.click(controls.stop);
    fireEvent.click(controls.restart);
    expect(controls.onAction).not.toHaveBeenCalled();
  });

  it("Started enables Stop and Restart but not Start", () => {
    const controls = renderControls(1);
    expect(controls.start).toBeDisabled();
    expect(controls.stop).toBeEnabled();
    expect(controls.restart).toBeEnabled();
    fireEvent.click(controls.stop);
    fireEvent.click(controls.restart);
    expect(controls.onAction).toHaveBeenNthCalledWith(1, "server-1", "stop");
    expect(controls.onAction).toHaveBeenNthCalledWith(2, "server-1", "restart");
  });

  it("Stopping disables every action", () => {
    const controls = renderControls(3);
    expect(controls.start).toBeDisabled();
    expect(controls.stop).toBeDisabled();
    expect(controls.restart).toBeDisabled();
  });

  it("loading disables the currently valid action", () => {
    const action = vi.fn();
    render(<DetailControls server={server(1, true)} loading onAction={action} />);
    const buttons = screen.getAllByRole("button");
    expect(buttons[2]).toBeDisabled();
    expect(action).not.toHaveBeenCalled();
  });
});
