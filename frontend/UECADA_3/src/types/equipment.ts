export interface Equipment {
  id: number
  equipmentCode: string
  equipmentName: string
  processType: string
  model: string | null
  installDate: string | null
  location: string | null
  locationX: number | null
  locationY: number | null
  createdAt: string | null
}

export type EquipmentStatusCode = 'IDLE' | 'RUNNING' | 'COMPLETE' | 'WARNING' | 'ERROR' | 'MAINTENANCE' | 'STANDBY' | 'ALARM' | string

export interface EquipmentStatusItem {
  equipId: string
  statusCode: EquipmentStatusCode
  updatedAt: string | null
}
