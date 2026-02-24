# Web Client Module

Jellyfin Web module for the AYSW prompt UX.

## Features

- Fetches effective config from plugin endpoint
- Tracks interaction events and episode transitions
- Shows blocking modal prompt with countdown
- Sends ack decisions to server
- Stops playback on stop/timeout

## Build

- `npm --prefix apps/web-client install`
- `npm --prefix apps/web-client run build`

Bundle output:
- `apps/web-client/dist/jellycheckr-web.js`
