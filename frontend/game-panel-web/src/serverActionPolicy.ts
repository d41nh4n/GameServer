export type ServerAction = "start" | "stop" | "restart";

export function isActionAllowed(status: number, action: ServerAction): boolean {
  if (status === 0) return action === "start";
  if (status === 1) return action === "stop" || action === "restart";
  return false;
}
