import { defineConfig } from 'vite'

export default defineConfig({
	server: {
		host: '0.0.0.0',
		port: 5174,
		strictPort: true,
		proxy: {
			'/api': {
				target: process.env.MOODIFY_API_TARGET || 'http://localhost:5170',
				changeOrigin: true,
			},
		},
	},
})