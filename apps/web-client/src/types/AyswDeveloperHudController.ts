import { AyswDeveloperHudSnapshot } from "../types/AyswDeveloperHudSnapshot";


export interface AyswDeveloperHudController {
  render(snapshot: AyswDeveloperHudSnapshot): void;
  pushEvent(message: string): void;
  dispose(): void;
}
