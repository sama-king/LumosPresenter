import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import '@fontsource-variable/inter/index.css'
import '@fontsource-variable/hanken-grotesk/index.css'
import '@fontsource-variable/jetbrains-mono/index.css'
// Stage display fonts — the picker itself is data-driven via GET /api/fonts.
import '@fontsource-variable/eb-garamond/index.css'
import '@fontsource-variable/lora/index.css'
import '@fontsource-variable/montserrat/index.css'
import '@fontsource-variable/source-serif-4/index.css'
import '@fontsource/bebas-neue/index.css'
import 'material-symbols/outlined.css'
import './index.css'
import App from './App.tsx'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <BrowserRouter>
      <App />
    </BrowserRouter>
  </StrictMode>,
)
