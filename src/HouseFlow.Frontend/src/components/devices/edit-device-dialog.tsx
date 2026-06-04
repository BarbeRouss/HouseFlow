"use client";

import { useState, useEffect } from "react";
import { useTranslations } from "next-intl";
import { useUpdateDevice } from "@/lib/api/hooks";
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card";
import { Button } from "@/components/ui/button";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";

interface EditDeviceDialogProps {
  deviceId: string;
  device: {
    name: string;
    type: string;
    brand?: string | null;
    model?: string | null;
    installDate?: string | null;
  };
  open: boolean;
  onClose: () => void;
}

const deviceTypes = [
  "Chaudière Gaz",
  "Chaudière Fioul",
  "Pompe à Chaleur",
  "Climatisation",
  "Poêle à Bois",
  "Chauffe-eau",
  "VMC",
  "Toiture",
  "Détecteur de Fumée",
  "Alarme",
  "Autre",
];

export function EditDeviceDialog({ deviceId, device, open, onClose }: EditDeviceDialogProps) {
  const t = useTranslations("devices");
  const tCommon = useTranslations("common");

  const [name, setName] = useState(device.name);
  const [type, setType] = useState(device.type);
  const [brand, setBrand] = useState(device.brand || "");
  const [model, setModel] = useState(device.model || "");
  const [installDate, setInstallDate] = useState(
    device.installDate ? device.installDate.split("T")[0] : ""
  );

  useEffect(() => {
    if (open) {
      setName(device.name);
      setType(device.type);
      setBrand(device.brand || "");
      setModel(device.model || "");
      setInstallDate(device.installDate ? device.installDate.split("T")[0] : "");
    }
  }, [open, device]);

  const updateMutation = useUpdateDevice(deviceId, {
    onSuccess: () => {
      onClose();
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    updateMutation.mutate({
      name,
      type,
      brand: brand || null,
      model: model || null,
      installDate: installDate || null,
    });
  };

  if (!open) return null;

  return (
    <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
      <Card className="w-full max-w-lg max-h-[90vh] overflow-y-auto">
        <CardHeader>
          <CardTitle>{t("editDevice")}</CardTitle>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSubmit} className="space-y-4">
            {updateMutation.isError && (
              <div className="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 px-4 py-3 rounded text-sm">
                {tCommon("createError")}
              </div>
            )}

            <div>
              <label htmlFor="edit-device-name" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                {t("deviceName")}
              </label>
              <input
                id="edit-device-name"
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                required
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
              />
            </div>

            <div>
              <label htmlFor="edit-device-type" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                {t("deviceType")}
              </label>
              <Select value={type} onValueChange={setType}>
                <SelectTrigger className="w-full">
                  <SelectValue placeholder={tCommon("selectType")} />
                </SelectTrigger>
                <SelectContent>
                  {deviceTypes.map((dt) => (
                    <SelectItem key={dt} value={dt}>{dt}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div>
                <label htmlFor="edit-device-brand" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                  {t("brand")} ({tCommon("optional")})
                </label>
                <input
                  id="edit-device-brand"
                  type="text"
                  value={brand}
                  onChange={(e) => setBrand(e.target.value)}
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                />
              </div>
              <div>
                <label htmlFor="edit-device-model" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                  {t("model")} ({tCommon("optional")})
                </label>
                <input
                  id="edit-device-model"
                  type="text"
                  value={model}
                  onChange={(e) => setModel(e.target.value)}
                  className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
                />
              </div>
            </div>

            <div>
              <label htmlFor="edit-device-installDate" className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">
                {t("installDate")} ({tCommon("optional")})
              </label>
              <input
                id="edit-device-installDate"
                type="date"
                value={installDate}
                onChange={(e) => setInstallDate(e.target.value)}
                className="w-full px-3 py-2 border border-gray-300 dark:border-gray-600 rounded-md shadow-sm focus:ring-2 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:text-white"
              />
            </div>

            <div className="flex gap-3 pt-2">
              <Button type="button" variant="outline" onClick={onClose} className="flex-1">
                {tCommon("cancel")}
              </Button>
              <Button type="submit" disabled={updateMutation.isPending} className="flex-1">
                {updateMutation.isPending ? tCommon("loading") : tCommon("save")}
              </Button>
            </div>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
