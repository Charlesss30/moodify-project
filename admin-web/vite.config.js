import { defineConfig } from 'vite'

export default defineConfig({
	server: {
		host: '0.0.0.0',
		port: 5174,
		strictPort: true,
		proxy: {
			'/api': {
				target: 'http://localhost:5170',
				changeOrigin: true,
			},
		},
	},
})