import * as dotenv from 'dotenv';
dotenv.config();

import {
  AnswerPayload,
  CandidatePayload,
  HostRoomPayload,
  JoinRoomPayload,
  OfferPayload,
  Room,
} from './signaling';
import crypto from 'crypto';
import { serverConfig } from './server.config';
import http from "http";
import express from "express";
import { Server } from "socket.io";

const app = express();
const httpServer = http.createServer(app);
const io = new Server(httpServer, {
  cors: {
    origin: '*',
  },
});

const rooms: Record<string, Room> = {};

function generateRoomId(): string {
  // 6-char base36 random
  return crypto.randomBytes(3).toString('hex');
}

// Auth Middleware
io.use((socket, next) => {
  console.log('New incoming request');

  const token = socket.handshake.auth.token || socket.handshake.query.token || socket.handshake.headers.authorization;
  if (token === serverConfig.auth.token) {
    next();
  } else {
    next(new Error('Authentication error'));
  }
});

io.on('connection', (socket) => {
  console.log(`Client connected: ${socket.id}`);

  socket.on('host-room', ({ roomId }: HostRoomPayload) => {
    if (roomId) {
      if (rooms[roomId]) {
        console.error(`Room already exists: ${roomId}`);
        socket.emit('host-room-failed', { reason: 'Room already exists' });
        return;
      }
    } else {
      let attempts = 0;
      do {
        roomId = generateRoomId();
        attempts++;
      } while (rooms[roomId] && attempts < 50); // Ensure unique

      if (rooms[roomId]) {
        console.error(`Unable to create a unique room`);
        socket.emit('host-room-failed', { reason: 'Cannot create a unique room' });
        return;
      }
    }

    rooms[roomId] = { host: socket.id, clients: [] };
    socket.join(roomId);
    console.log(`Host ${socket.id} created room ${roomId}`);

    socket.emit('room-created', { roomId });
  });

  socket.on('join-room', ({ roomId }: JoinRoomPayload) => {
    if (!rooms[roomId]) {
      console.error(`Room ${roomId} does not exist!`);
      socket.emit('room-not-found', { roomId });
      return;
    }
    rooms[roomId].clients.push(socket.id);
    socket.join(roomId);
    console.log(`Client ${socket.id} joined room ${roomId}`);
    io.to(rooms[roomId].host).emit('new-client', { socketId: socket.id });
  });

  // Web-RTC Setup
  socket.on('offer', ({ to, sdp }: OfferPayload) => {
    console.log(`Forwarding offer: ${socket.id} -> ${to}`);
    io.to(to).emit('offer', { from: socket.id, sdp });
  });

  socket.on('answer', ({ to, sdp }: AnswerPayload) => {
    console.log(`Forwarding answer: ${socket.id} -> ${to}`);
    io.to(to).emit('answer', { from: socket.id, sdp });
  });

  socket.on('candidate', ({ to, candidate, roomId }: CandidatePayload) => {
    if (to && to !== '') {
      // host sending to a specific client
      io.to(to).emit('candidate', { from: socket.id, candidate });
    } else if (roomId && rooms[roomId]) {
      // client sending to host
      const hostId = rooms[roomId].host;
      io.to(hostId).emit('candidate', { from: socket.id, candidate });
    }
  });

  // Client Disconnects
  socket.on('disconnect', () => {
    console.log(`Client disconnected: ${socket.id}`);
    for (const roomId in rooms) {
      const room = rooms[roomId];
      if (room.host === socket.id) {
        // Notify all clients host is gone
        io.to(roomId).emit('host-disconnected', {});
        delete rooms[roomId];
      } else {
        io.to(room.host).emit('client-disconnected', { socketId: socket.id });
        room.clients = room.clients.filter((id) => id !== socket.id);
      }
    }
  });
});

app.get('/', (req, res) => {
  res.send('WebRTC signaling server')
})

httpServer.listen(serverConfig.port);

console.log(`Server is now running on Port ${serverConfig.port}...`);
